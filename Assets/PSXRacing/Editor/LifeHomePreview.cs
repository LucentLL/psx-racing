using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Renders the LifeSim menu to PNGs at several aspect ratios, without play
    /// mode, so layout can be checked before a build reaches a phone.
    ///
    /// This exists because the menu is generated entirely at runtime: nothing is
    /// visible in the scene view, the editor Game view is one aspect ratio, and
    /// the bug that actually shipped — the body panel riding up over the tab bar
    /// — only appears on a canvas shorter than about 718 units, which is to say
    /// only on a wide phone. Compiling proves nothing about layout.
    /// </summary>
    public static class LifeHomePreview
    {
        // The first entry is the reporter's handset aspect (~2.24:1), which is
        // where the overlap showed up.
        static readonly (string name, int w, int h)[] Sizes =
        {
            ("phone_wide", 1998, 891),
            ("landscape_16x9", 1280, 720),
            ("tablet_4x3", 1024, 768),
        };

        [MenuItem("PSX Racing/Preview LifeSim Menu")]
        public static void Capture()
        {
            string outDir = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(outDir);

            // No save -> the wizard. Capture that at every aspect first.
            LifeSimManager.DeleteSave();
            Shoot(outDir, "wizard");

            // Then seed a running game and capture the screens that actually get
            // used day to day — the home hub was where the overlap was reported.
            LifeSimManager.StartNewGame("VINCE", 25, LifeRules.DefaultJobIndex);
            // Step two of the wizard, WHAT ARE YOU DRIVING? - the starter lanes
            // (the 240SX and an EG Civic, rough and tidy, then a new car).
            Shoot(outDir, "carpick");
            LifeRules.SeedFallbackCar(LifeSimManager.State);
            var st = LifeSimManager.State;

            // FOUR DAYS OF A CAREER before anything is photographed, so the
            // week grid has a past to draw: a shift worked, a shift skipped for
            // a race, a morning slept through, a day out in the city, an
            // inspection. A fresh save's week is seven columns of "—" and
            // proves nothing about the words the cells are supposed to carry.
            LifeRules.Sleep(st);                                                             // FRI morning
            LifeRules.ClockOnShift(st); LifeRules.SpendActivitySlot(st, LifeRules.ActWork);  // FRI day
            LifeRules.SpendActivitySlot(st, LifeRules.ActRace);                              // FRI night: shift skipped
            LifeRules.SpendActivitySlot(st, LifeRules.ActDrive);                             // SAT morning
            LifeRules.ClockOnShift(st); LifeRules.SpendActivitySlot(st, LifeRules.ActWork);  // SAT day
            LifeRules.Sleep(st);                                                             // SAT night
            LifeRules.SpendActivitySlot(st, LifeRules.ActInspect);                           // SUN morning
            LifeRules.Sleep(st); LifeRules.Sleep(st);                                        // SUN day, night
            LifeRules.Sleep(st);                                                             // MON morning
            LifeRules.ClockOnShift(st); LifeRules.SpendActivitySlot(st, LifeRules.ActWork);  // MON day
            LifeRules.Sleep(st);                                                             // MON night -> TUE 5 JAN
            // Four days without a meal would have the header reading CRITICAL;
            // the shots are about layout, not the hunger ladder.
            st.health = 100f; st.daysSinceEat = 0; st.foodStock = 4;

            // Something in the diary before anything is photographed: a race in
            // TONIGHT's block (the gold RACE row on the hub, the RACE DAY
            // pre-race page), one in a DAY block two days out (the planner's
            // CANCEL state, and a race that skips a shift), one nine days out.
            LifeRules.Book(st, st.day, LifeRules.NightSlot, 1, false);
            LifeRules.Book(st, st.day + 2, LifeRules.DaySlot, 3, false);
            LifeRules.Book(st, st.day + 9, LifeRules.NightSlot, 4, false);
            LifeSimManager.Save();

            Shoot(outDir, "home");

            // MAIN once per BLOCK of the day. The race row, the shift button
            // and the sleep caption all say something different in each one,
            // and a screen shot in one state proves nothing about the others.
            // mustFit: "all of it on one screen" is a REQUIREMENT of the day
            // view, and a requirement nothing checks is one that decays.
            for (int slot = 0; slot < LifeRules.SlotNames.Length; slot++)
            {
                st.slotIndex = slot;
                LifeSimManager.Save();
                Shoot(outDir, "home_" + LifeRules.SlotNames[slot].ToLower(), "main",
                      mustFit: true);
            }
            st.slotIndex = 0;
            LifeSimManager.Save();

            // The planner, in its shapes: a block that holds a race (CANCEL),
            // an open shift block (BOOK, with the skips-the-shift warning), and
            // a block already spent (the record's words). All three are the DAY
            // view with the cursor moved, so they have to fit as well.
            Shoot(outDir, "home_plan_booked", "main", mustFit: true,
                  calDay: st.day + 2, calSlot: LifeRules.DaySlot);
            Shoot(outDir, "home_plan_open", "main", mustFit: true,
                  calDay: st.day + 4, calSlot: LifeRules.DaySlot);
            Shoot(outDir, "home_plan_past", "main", mustFit: true,
                  calDay: st.day - 4, calSlot: LifeRules.NightSlot);

            // The two grids. The week fits on every canvas; the month gives up
            // a unit or two to the scroll on a phone by design, so it is not
            // held to mustFit.
            Shoot(outDir, "week", "main", mustFit: true, calView: "Week");
            Shoot(outDir, "month", "main", calView: "Month");

            // The pre-race page with the booking in this block, and without.
            // Both are the PLANNER now — reached from the house, they write the
            // race into the diary and nothing else. The launcher half of the
            // page (START, one read-only car) only exists on the far side of a
            // zone line, which no menu preview can stand at.
            st.slotIndex = LifeRules.NightSlot;
            LifeSimManager.Save();
            Shoot(outDir, "prerace_booked", "prerace");
            st.slotIndex = 0;
            LifeSimManager.Save();
            Shoot(outDir, "prerace_open", "prerace");

            // THE ONE DOOR OUT OF THE HOUSE, and the page behind it. Shot on a
            // meet night as well as an ordinary one, because the line under
            // DRIVE is the only thing left on MAIN that says a meet is on — the
            // button that used to announce it is gone.
            Shoot(outDir, "drive", "drive");
            // A MEET NIGHT, which has to be arranged rather than assumed: the
            // save is aged four days before any of this, and CarMeets.MeetOn is
            // Friday and Saturday only — so the ordinary shot above lands on a
            // Tuesday and proves nothing about the row that says CAR MEET.
            int preMeetDay = st.day, preMeetSlot = st.slotIndex;
            while (!CarMeets.MeetOn(st.day)) st.day++;
            st.slotIndex = LifeRules.NightSlot;
            LifeSimManager.Save();
            Shoot(outDir, "drive_meet", "drive");
            Shoot(outDir, "home_meet_drive", "main", mustFit: true);
            st.day = preMeetDay; st.slotIndex = preMeetSlot;
            LifeSimManager.Save();

            // Debug mode: the six garage slots the second-car shots below need,
            // and the DEBUG rung on OPTIONS.
            LifeRules.EnableDebug(LifeSimManager.State);
            LifeSimManager.Save();

            // A SECOND car, because the garage's car switcher only draws when
            // there is something to switch between — a one-car garage renders
            // identically with and without it, so the single-car capture proves
            // nothing about the row that was just added.
            if (CarCatalog.Ready && CarCatalog.All.Count > 1)
            {
                CarMarket.MakeOwnedCar(LifeSimManager.State, CarCatalog.All[1], 88, 41000f, 12500);
                LifeSimManager.Save();
                Shoot(outDir, "garage_multi", "garage");
                Shoot(outDir, "debugcars", "debugcars");
                var extra = LifeSimManager.State.cars[LifeSimManager.State.cars.Count - 1];
                LifeSimManager.State.cars.Remove(extra);
            }

            // MY CARS WITH EVERY PLACE ON IT. Six cars: the one being driven
            // (garage), one on the drive, one in the yard, and one each at the
            // mechanic, the dealership and the paint shop — which is every
            // word the status bar can print and both of its shapes (a place,
            // and a place + UNAVAILABLE + a ready time). The jobs are written
            // straight into the queue rather than booked, because booking
            // needs a fault to book and these shots are about the page.
            if (CarCatalog.Ready && CarCatalog.All.Count > 40)
            {
                var ds = LifeSimManager.State;
                var added = new System.Collections.Generic.List<OwnedCar>();
                foreach (int pick in new[] { 3, 9, 17, 25, 33 })
                    added.Add(CarMarket.MakeOwnedCar(ds, CarCatalog.All[pick], 40 + pick, 1000f * pick,
                                                     CarCatalog.All[pick].price));
                ds.pendingParts.Add(new PendingPart
                {
                    carId = added[2].id, faultId = "preview", label = "Slipping clutch",
                    stat = "engine", readyDay = ds.day + 2, venue = CarWhere.VenueMechanic,
                });
                ds.pendingParts.Add(new PendingPart
                {
                    carId = added[3].id, faultId = "preview2", label = "Warped brake rotors",
                    stat = "tires", readyDay = ds.day, readySlot = ds.slotIndex + 1,
                    venue = CarWhere.VenueDealer,
                });
                ds.pendingParts.Add(new PendingPart
                {
                    carId = added[4].id, label = "RESPRAY — MIDNIGHT BLUE", stat = "paint",
                    readyDay = ds.day + 1, venue = CarWhere.VenuePaint,
                });
                LifeSimManager.Save();
                Shoot(outDir, "mycars", "garage");
                Shoot(outDir, "mycars_list", "garage", scrollTo: 0f);
                Shoot(outDir, "carmenu_away", "carmenu", garageCar: added[2].id);
                Shoot(outDir, "prerace_away", "prerace", scrollTo: 0.4f);
                // The calendar's own entries for those three: the block each
                // car comes back in, in the day view, the week and the month.
                Shoot(outDir, "home_plan_pickup", "main", mustFit: true,
                      calDay: ds.day + 2, calSlot: LifeRules.MorningSlot);
                Shoot(outDir, "week_cars", "main", mustFit: true, calView: "Week");
                Shoot(outDir, "month_cars", "main", calView: "Month");

                // And the ONE-CAR household with its car in the shop: the top
                // of MY CARS is then a car that is not there, and MAIN's two
                // drives are shut.
                string wasActive = ds.activeCar;
                ds.activeCar = added[2].id;
                LifeSimManager.Save();
                Shoot(outDir, "mycars_away", "garage");
                Shoot(outDir, "home_car_away", "main", mustFit: true);
                ds.activeCar = wasActive;

                ds.pendingParts.RemoveAll(p => added.Exists(c => c.id == p.carId));
                foreach (var c in added) ds.cars.Remove(c);
                LifeSimManager.Save();
            }

            // A MEET NIGHT. The clock is wound to the first Friday night the
            // career has left (the save is at TUE 5 JAN, so FRI 8): the hub's
            // town row becomes the meet, and the block on the left says so.
            // Then the planner looking AT a meet from earlier in the week.
            {
                var ms = LifeSimManager.State;
                int wasDay = ms.day, wasSlot = ms.slotIndex;
                int meetDay = CarMeets.NextMeetDay(ms.day);
                Shoot(outDir, "home_plan_meet", "main", mustFit: true,
                      calDay: meetDay, calSlot: CarMeets.MeetSlot);
                ms.day = meetDay; ms.slotIndex = CarMeets.MeetSlot;
                LifeSimManager.Save();
                Shoot(outDir, "home_meet_night", "main", mustFit: true);
                ms.day = wasDay; ms.slotIndex = wasSlot;
                LifeSimManager.Save();
            }

            LifeSimManager.State.debugMode = false;
            LifeSimManager.State.garageSlots = 1;
            LifeSimManager.Save();

            // The blacklist board is the tallest screen in the game — ELEVEN
            // rows now that the player is one of them, plus a header, a news
            // strip and, when somebody has called you out, a card — so it is
            // the one most likely to run off the bottom of a short canvas.
            //
            // Stand the player in the MIDDLE of it. A board shot with the
            // player on the bottom rung is a board with nothing below them,
            // which is half the screen's states missing: no name that can call
            // them out, and no row drawn in the "below you" ink.
            var s = LifeSimManager.State;
            s.streetRacesWon = 3;
            s.streetRep = 10f;
            Blacklist.SeedBoard(s);
            {
                var bd = Blacklist.Board(s);
                var you = bd[bd.Count - 1];
                bd.RemoveAt(bd.Count - 1);
                bd.Insert(5, you);
                // Records, so the W-L column is not eleven zeroes — it is the
                // column that carries the churn and it has to be legible.
                for (int i = 0; i < bd.Count; i++)
                {
                    bd[i].wins = 14 - i;
                    bd[i].losses = 3 + (i % 5);
                }
                s.blNews.Clear();
                s.blNews.Add(LifeRules.LogDate(s.day) + ": KAZE takes #7 off BIG SAL (2-1)");
                s.blNews.Add(LifeRules.LogDate(s.day) + ": GHOST holds #2 against PREACHER (2-0)");
            }
            // Give the garage something to show: a worn car with a real fault is
            // the state the repair options appear in, and those options going
            // off-screen is exactly what got reported.
            var car = s.ActiveCar;
            if (car != null)
            {
                car.engine = 57f; car.tires = 38f; car.carHP = 41f;
                car.paint = 51f; car.fuel = 43f;
                if (car.faults.Count == 0)
                {
                    var f = FaultCatalog.RollWearFault(car, "tires", false);
                    // Revealed by hand: faults now arrive hidden, and the whole
                    // point of this shot is the repair row's LAYOUT, which a
                    // hidden fault does not draw.
                    if (f != null) { f.hidden = false; f.diagnosed = true; car.faults.Add(f); }
                }
            }
            s.money = 4841;

            LifeSimManager.Save();

            // Every tab, not just the two that had been looked at. Three of the
            // four bugs found here were on tabs nobody had rendered.
            foreach (var t in new[] { "rivals", "garage", "news", "options",
                                      "market", "junkyard", "dealer", "eat", "bills", "jobs",
                                      "inspect", "inspectfocus", "toolbox",
                                      // The garage is a LIST now and the car page is where
                                      // everything you can do to a car lives, so the tab shot
                                      // no longer covers either of them.
                                      "carmenu", "specs",
                                      // The two trades the town now has an
                                      // address for. PAINT is a grid of colour
                                      // chips over a turntable and is the only
                                      // page in the menu whose height depends
                                      // on how many liveries a shell happens
                                      // to carry — which is exactly the kind of
                                      // page that fits on the car it was
                                      // written against and runs off the
                                      // bottom on the next one.
                                      "service", "paint",
                                      // The setup screen on a car with nothing
                                      // fitted: every row padlocked. That is the
                                      // state most players see first, and the
                                      // one where "does every padlock name its
                                      // part" is actually checkable.
                                      "setup" })
                Shoot(outDir, t, t);

            // THE BOARD WITH A CALL-OUT ON IT — the state the RIVALS page is in
            // whenever it matters, and the only one that draws the series card,
            // the deadline and the two buttons that settle it. Shot in both
            // directions: an incoming call-out is the one that can cost a rank,
            // and it is drawn in a different ink for exactly that reason.
            {
                s.blChallenge = new RankChallenge();
                Blacklist.ChallengeDown(s);
                s.blChallenge.themLegs = 1;   // a race down, which is the state worth looking at
                LifeSimManager.Save();
                Shoot(outDir, "rivals_defend", "rivals");
                // And MAIN with the same call-out live: the hub grows a banner
                // above the race row on the days one is running, and a row
                // added to the busiest column in the game is exactly the kind
                // of thing that pushes SLEEP under the fold.
                Shoot(outDir, "home_callout", "main", mustFit: true);

                s.blChallenge = new RankChallenge();
                Blacklist.ChallengeUp(s);
                s.blChallenge.youLegs = 1;
                LifeSimManager.Save();
                Shoot(outDir, "rivals_callout", "rivals");

                s.blChallenge = new RankChallenge();
                LifeSimManager.Save();
            }

            // SPECS and the car page again, on a car that HAS a catalog entry.
            // The seeded starter RX-7 deliberately has none — it is the one car
            // in the game written by hand rather than baked from the catalog —
            // so shooting these off it renders the "nothing on paper" fallback
            // and proves nothing about the table that is the point of the page.
            if (CarCatalog.Ready && CarCatalog.All.Count > 1)
            {
                string wasActive = s.activeCar;
                s.garageSlots = Mathf.Max(s.garageSlots, s.cars.Count + 1);
                var speccd = CarMarket.MakeOwnedCar(s, CarCatalog.All[1], 74, 22000f, 18400);
                s.activeCar = speccd.id;
                LifeSimManager.Save();
                Shoot(outDir, "specs_catalog", "specs");
                Shoot(outDir, "carmenu_catalog", "carmenu");
                Shoot(outDir, "tune_catalog", "tune");
                // THE TWO POWER PATHS on an NA road car: the TURBO KIT row
                // offered beside the NA ladder, then - once on the turbo path -
                // the REVERT ENGINE row that reopens the choice.
                CarSpec naSpec = null;
                foreach (var c in CarCatalog.All)
                    if (c.CanFitTurboKit && c.builtHp > c.hp && CarModelLibrary.LoadFor(c) != null) { naSpec = c; break; }
                if (naSpec != null)
                {
                    s.garageSlots = Mathf.Max(s.garageSlots, s.cars.Count + 1);
                    var naCar = CarMarket.MakeOwnedCar(s, naSpec, 80, 15000f, 12000);
                    s.activeCar = naCar.id;
                    LifeSimManager.Save();
                    Shoot(outDir, "tune_turbo_choice", "tune", scrollTo: 1f);
                    naCar.turbo = true;
                    naCar.upPower = 2;
                    LifeSimManager.Save();
                    Shoot(outDir, "tune_turbo_path", "tune", scrollTo: 1f);
                    s.activeCar = speccd.id;
                }
                // The setup screen on a car with NOTHING fitted: every row
                // padlocked, each naming the part that opens it. That is the
                // first thing a player sees and the whole unlock story, and the
                // bare "setup" shot cannot cover it — the seeded starter car has
                // no catalog entry and renders the fallback instead.
                Shoot(outDir, "setup_stock", "setup");

                // The setup screen with the parts actually fitted, which is the
                // only state that photographs a live stepper row — the bare
                // "setup" shot above is all padlocks by design. Every stage
                // maxed and every mod bolted on, so all six pages have
                // something to draw and nothing is hiding behind a gate.
                speccd.upPower = speccd.upWeight = speccd.upBrakes = 4;
                speccd.upSuspension = speccd.upTires = 4;
                speccd.swayBars = speccd.steeringRack = speccd.lsd = true;
                speccd.finalDriveSet = speccd.gearSet = speccd.aeroKit = true;
                LifeSimManager.Save();
                Shoot(outDir, "setup_tires", "setup");
                foreach (var pg in new[] { SetupPage.Alignment, SetupPage.Springs,
                                           SetupPage.Differential, SetupPage.Gearing,
                                           SetupPage.Aero })
                    ShootSetupPage(outDir, "setup_" + pg.ToString().ToLower(), pg);

                // The parts page and the spec sheet with a POWER build on, which
                // is where the top speed says "stock -> built (+N%)" and the next
                // power stage quotes the top speed it buys (2026-09-21).
                speccd.upPower = 2;
                speccd.supercharged = false;
                LifeSimManager.Save();
                Shoot(outDir, "tune_power2", "tune");
                Shoot(outDir, "specs_power2", "specs");

                // And a RACE CAR, which the shop no longer sells anything but a
                // seat: the page has to say what it came with instead.
                CarSpec raceSpec = null;
                foreach (var c in CarCatalog.All) if (c.IsRaceCar) { raceSpec = c; break; }
                if (raceSpec != null)
                {
                    var racer = CarMarket.MakeOwnedCar(s, raceSpec, 90, 3000f, raceSpec.price);
                    s.garageSlots = Mathf.Max(s.garageSlots, s.cars.Count + 1);
                    s.activeCar = racer.id;
                    LifeSimManager.Save();
                    Shoot(outDir, "tune_racecar", "tune");
                    Shoot(outDir, "specs_racecar", "specs");
                    s.cars.Remove(racer);
                }

                s.cars.Remove(speccd);
                s.activeCar = wasActive;
                s.garageSlots = 1;
                LifeSimManager.Save();
            }

            // The seller's conversation. It cannot be shot by naming the tab —
            // BuildViewing bounces straight back to the classifieds without a
            // visit open — so one is opened here first, on the roughest car in
            // the paper, with the walk-round already done so the page has some
            // findings to lay out. That is the state the layout can go wrong in.
            if (CarCatalog.Ready && s.newspaper.Count > 0)
            {
                CarListing worst = s.newspaper[0];
                foreach (var l in s.newspaper) if (l.cond < worst.cond) worst = l;
                var visit = Viewings.Open(s, worst, "paper");
                Viewings.LookOver(s, visit);
                s.activeViewing = visit.key;
                LifeSimManager.Save();
                Shoot(outDir, "viewing", "viewing");
                s.activeViewing = "";
                s.viewings.Remove(visit);
                LifeSimManager.Save();
            }

            // OPTIONS took NEW GAME and the debug rung off the launch screen, so
            // it is now the tallest of the settings pages and the one where
            // something can fall off the end. Shot from the bottom of the
            // scroll, where those two live.
            Shoot(outDir, "options_meta", "options", scrollTo: 0f);

            // The condition bars, which are below the fold on the CAR page and
            // are the whole subject of "condition % should not be shown outside
            // debug mode". A shot from the top of the scroll shows the
            // turntable and proves nothing about them.
            Shoot(outDir, "carmenu_bars", "carmenu", scrollTo: 0.45f);

            LifeSimManager.DeleteSave();
        }

        /// <param name="scrollTo">Where to leave the body's scroll before the
        /// shutter: 1 is the top, 0 the bottom. A page taller than the canvas
        /// cannot be photographed whole, so the shots that care about what is
        /// underneath say where to look.</param>
        /// <param name="mustFit">Assert that this page does NOT scroll on any
        /// aspect. "All of it on one screen" is a REQUIREMENT of the launch
        /// screen, not a nicety, and a requirement nothing checks is one that
        /// decays the next time a line of text is added to the page. A PNG
        /// cannot show what is below the fold; this can.</param>
        /// <summary>One of the setup screen's six sub-pages. Six shots off one
        /// tab, because the sub-strip is inside the page rather than on the tab
        /// bar and the shot list only knows about tabs.</summary>
        static void ShootSetupPage(string outDir, string label, SetupPage page) =>
            Shoot(outDir, label, "setup", setupPage: page);

        /// <param name="calView">"Week" or "Month" to shoot MAIN as one of its
        /// grids rather than as the day view.</param>
        /// <param name="calDay">With calSlot, where to put the calendar's
        /// cursor before the page is built — the planner only exists for a
        /// block that is not NOW.</param>
        /// <param name="garageCar">Which owned car (OwnedCar.id) a car page is
        /// about. Without it the page falls back to the active car, which is
        /// never the one that is away at a shop.</param>
        /// <summary>Clipped-text findings over the whole run.</summary>
        internal static int clipCount;

        /// <summary>
        /// "No text should clip in the game" (owner, 2026-09-26). Every visible
        /// UI Text on the screen, measured by its GLYPHS - the text generator's
        /// own quads, not the rect it was given, since most labels here
        /// overflow their rect on purpose - against three things:
        ///   * the screen: a glyph past an edge is cut off by the device;
        ///   * any RectMask2D above it, sideways only - a scroll list clips
        ///     rows above and below it by design, and those are a scroll away;
        ///   * truncation: a wrapping, truncating label that drew fewer
        ///     characters than it holds.
        /// Logged as "[HomePreview] CLIP", with the text and how far over.
        /// </summary>
        internal static void ReportClippedText(Camera cam, string where, int w, int h)
        {
            const float Slack = 2f;   // pixels: antialiased glyph edges
            foreach (var t in Object.FindObjectsByType<Text>(FindObjectsSortMode.None))
            {
                if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text) || t.color.a < 0.05f) continue;
                var gen = t.cachedTextGenerator;
                if (gen == null || gen.vertexCount == 0) continue;
                float unitsPerPixel = 1f / Mathf.Max(0.0001f, t.pixelsPerUnit);
                var verts = gen.verts;
                float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
                for (int i = 0; i < verts.Count; i++)
                {
                    Vector3 world = t.rectTransform.TransformPoint(verts[i].position * unitsPerPixel);
                    Vector2 sp = RectTransformUtility.WorldToScreenPoint(cam, world);
                    xMin = Mathf.Min(xMin, sp.x); xMax = Mathf.Max(xMax, sp.x);
                    yMin = Mathf.Min(yMin, sp.y); yMax = Mathf.Max(yMax, sp.y);
                }
                string snippet = t.text.Replace("\n", " ");
                if (snippet.Length > 48) snippet = snippet.Substring(0, 48) + "…";
                var issues = new List<string>();

                // Scrolled content is judged against its viewport, not the
                // screen: a row below the fold is a scroll away, not clipped.
                var mask = t.GetComponentInParent<RectMask2D>();
                bool inScroll = mask != null && t.GetComponentInParent<ScrollRect>() != null;
                if (!inScroll)
                {
                    if (xMin < -Slack) issues.Add("off the LEFT edge by " + (-xMin).ToString("0") + " px");
                    if (xMax > w + Slack) issues.Add("off the RIGHT edge by " + (xMax - w).ToString("0") + " px");
                    if (yMin < -Slack) issues.Add("off the BOTTOM by " + (-yMin).ToString("0") + " px");
                    if (yMax > h + Slack) issues.Add("off the TOP by " + (yMax - h).ToString("0") + " px");
                }
                if (mask != null)
                {
                    var c = new Vector3[4];
                    mask.rectTransform.GetWorldCorners(c);
                    Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]);
                    Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
                    bool visibleRow = yMax > a.y && yMin < b.y;   // on screen in the list
                    if (visibleRow && xMin < a.x - Slack) issues.Add("cut on the LEFT by its panel, " + (a.x - xMin).ToString("0") + " px");
                    if (visibleRow && xMax > b.x + Slack) issues.Add("cut on the RIGHT by its panel, " + (xMax - b.x).ToString("0") + " px");
                    if (!inScroll && yMax > b.y + Slack) issues.Add("cut at the TOP by its panel");
                    if (!inScroll && yMin < a.y - Slack) issues.Add("cut at the BOTTOM by its panel");
                }
                int shown = gen.characterCountVisible;
                int plain = System.Text.RegularExpressions.Regex.Replace(t.text, "<[^>]+>", "").Replace("\n", "").Length;
                if (t.verticalOverflow == VerticalWrapMode.Truncate && shown < plain - 1)
                    issues.Add("TRUNCATED: " + shown + " of " + plain + " characters drawn");
                // For the overlap test, only the part of the text the player
                // can SEE: a scrolled row that has slid under the page header
                // is hidden by its list's mask, not printed over the header.
                var box = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
                if (mask != null)
                {
                    var mc = new Vector3[4];
                    mask.rectTransform.GetWorldCorners(mc);
                    Vector2 m0 = RectTransformUtility.WorldToScreenPoint(cam, mc[0]);
                    Vector2 m1 = RectTransformUtility.WorldToScreenPoint(cam, mc[2]);
                    float bx0 = Mathf.Max(box.xMin, m0.x), by0 = Mathf.Max(box.yMin, m0.y);
                    float bx1 = Mathf.Min(box.xMax, m1.x), by1 = Mathf.Min(box.yMax, m1.y);
                    box = bx1 > bx0 && by1 > by0 ? Rect.MinMaxRect(bx0, by0, bx1, by1) : Rect.zero;
                }
                if (box.width > 0f && box.height > 0f)
                    glyphBoxes.Add((box, snippet, inScroll ? mask : null));
                if (issues.Count == 0) continue;
                clipCount++;
                Debug.LogWarning("[HomePreview] CLIP " + where + "  \"" + snippet + "\"  " + string.Join("; ", issues));
            }

            // TEXT ON TEXT. Not clipping, but the same failure to a reader:
            // the first pass at wrapping the parts page's long rows put them
            // over the row headings, and no clip test could see it. Two glyph
            // boxes overlapping by more than a third of the smaller one's
            // HEIGHT, and by a real width, are two lines printed over each
            // other.
            for (int i = 0; i < glyphBoxes.Count; i++)
                for (int j = i + 1; j < glyphBoxes.Count; j++)
                {
                    var a = glyphBoxes[i].box; var b = glyphBoxes[j].box;
                    float ox = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
                    float oy = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                    if (ox < 8f || oy < 0.34f * Mathf.Min(a.height, b.height)) continue;
                    clipCount++;
                    Debug.LogWarning("[HomePreview] CLIP " + where + "  OVERLAP  \"" + glyphBoxes[i].text +
                                     "\"  over  \"" + glyphBoxes[j].text + "\"");
                }
            glyphBoxes.Clear();
        }

        static readonly List<(Rect box, string text, RectMask2D scroll)> glyphBoxes =
            new List<(Rect, string, RectMask2D)>();

        static void Shoot(string outDir, string label, string tab = null, float scrollTo = 1f,
                          bool mustFit = false, SetupPage? setupPage = null,
                          string calView = null, int calDay = 0, int calSlot = -1,
                          string garageCar = null)
        {
            foreach (var size in Sizes)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                var camGO = new GameObject("PreviewCam");
                var cam = camGO.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.04f, 0.09f);
                cam.orthographic = true;

                var rt = new RenderTexture(size.w, size.h, 24, RenderTextureFormat.ARGB32)
                { antiAliasing = 1 };
                cam.targetTexture = rt;

                // Tell MenuKit what device this is BEFORE anything is built.
                // Screen still reports the batchmode editor window, not this
                // RenderTexture, so without the override every shot would be
                // laid out for whatever the editor happens to be — which is how
                // a "phone" capture came back showing the desktop column.
                MenuKit.ScreenSizeOverride = new Vector2(size.w, size.h);

                var host = new GameObject("LifeHome");
                var screen = host.AddComponent<LifeHomeScreen>();

                // Which tab to shoot is chosen BEFORE Start, not by rebuilding
                // after it. Rebuild tears the old body down with Destroy(), which
                // is deferred to the end of a frame that never comes outside play
                // mode — so a post-Start switch photographed both tabs stacked on
                // top of each other. Setting the field first means only the
                // wanted screen is ever built.
                if (tab != null)
                {
                    var tabField = typeof(LifeHomeScreen).GetField("tab",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (tabField == null) Debug.LogError("[HomePreview] no tab field");
                    else tabField.SetValue(screen, tab);
                }

                if (garageCar != null) SetField(screen, "garageCarId", garageCar);

                // Same trick, one level down: the setup screen's sub-page is
                // internal state, and switching it after Start would stack two
                // pages on top of each other for exactly the reason above.
                if (setupPage.HasValue)
                {
                    var pageField = typeof(LifeHomeScreen).GetField("setupPage",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (pageField == null) Debug.LogError("[HomePreview] no setupPage field");
                    else pageField.SetValue(screen, setupPage.Value);
                }

                // The calendar's view and cursor, one level down again. The
                // cursor is set WITH the clock it was placed under: SeedCalendar
                // throws a cursor away when the clock has moved since it was
                // set, and a cursor set with no clock behind it has "moved"
                // from nothing.
                if (calView != null || calDay > 0)
                {
                    // The clock FIRST, for both: SeedCalendar resets the view
                    // as well as the cursor when the clock has moved, and the
                    // first render of this harness shot the day view three
                    // times over under the labels "week" and "month" because
                    // only the cursor shots were setting it.
                    var st = LifeSimManager.State;
                    SetField(screen, "calClockDay", st.day);
                    SetField(screen, "calClockSlot", st.slotIndex);
                    SetField(screen, "calSelDay", calDay > 0 ? calDay : st.day);
                    SetField(screen, "calSelSlot", calDay > 0 ? calSlot : st.slotIndex);
                }
                if (calView != null)
                {
                    var viewField = typeof(LifeHomeScreen).GetField("calView",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (viewField == null) Debug.LogError("[HomePreview] no calView field");
                    else viewField.SetValue(screen, System.Enum.Parse(viewField.FieldType, calView));
                }

                // Start() is where the whole UI is constructed. Editor scripts do
                // not get lifecycle callbacks, so call it directly.
                var start = typeof(LifeHomeScreen).GetMethod("Start",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (start == null) { Debug.LogError("[HomePreview] no Start()"); return; }
                start.Invoke(screen, null);

                // The UI builds a ScreenSpaceOverlay canvas, which ignores
                // cameras and render textures. Re-point it at the preview camera
                // so it composites into the RT at the size we asked for.
                foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = cam;
                    c.planeDistance = 10f;

                    // CanvasScaler's ScaleWithScreenSize reads Screen too, so it
                    // would scale for the editor window rather than for this
                    // RenderTexture. Pin the factor by hand: RT pixels divided by
                    // the design column gives exactly the unit space the layout
                    // was written against.
                    var cs = c.GetComponent<CanvasScaler>();
                    if (cs != null)
                    {
                        cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                        cs.scaleFactor = size.h / MenuKit.DesignHeight;
                    }
                }
                Canvas.ForceUpdateCanvases();

                ReportClippedText(cam, label + "/" + size.name, size.w, size.h);

                // Report whether this screen can actually be scrolled to the
                // bottom. A static render cannot show that, and "the options are
                // off screen" turned out to be a scroll that received no drag
                // events rather than a layout that was too tall.
                var sr = Object.FindFirstObjectByType<UnityEngine.UI.ScrollRect>();
                if (sr != null && scrollTo < 1f)
                {
                    sr.verticalNormalizedPosition = Mathf.Clamp01(scrollTo);
                    Canvas.ForceUpdateCanvases();
                }
                if (sr != null && tab != null)
                {
                    float contentH = sr.content != null ? sr.content.sizeDelta.y : 0f;
                    float viewH = sr.viewport != null ? sr.viewport.rect.height : 0f;
                    var g = sr.GetComponent<UnityEngine.UI.Graphic>();
                    bool draggable = g != null && g.raycastTarget;
                    // A content rect is never sized SHORTER than its viewport
                    // (FitScrollContent floors it), so anything past a unit or
                    // two of slack is real overflow rather than rounding.
                    bool scrolls = contentH > viewH + 1f;
                    string line = "[HomePreview] " + label + "/" + size.name +
                                  " content " + contentH.ToString("0") + " vs view " +
                                  viewH.ToString("0") + (scrolls ? "  SCROLLS" : "  fits") +
                                  (draggable ? "  drag-catcher OK" : "  NO DRAG CATCHER");
                    if (mustFit && scrolls)
                        Debug.LogError(line + "  <-- MUST FIT ON ONE SCREEN, and does not");
                    else Debug.Log(line);
                }

                CheckNavReach(screen, label + "/" + size.name);

                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(size.w, size.h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, size.w, size.h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                string path = Path.Combine(outDir, "menu_" + label + "_" + size.name + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log("[HomePreview] wrote " + path);

                Object.DestroyImmediate(tex);
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                MenuKit.ScreenSizeOverride = Vector2.zero;
            }
        }

        /// <summary>
        /// Can a pad actually REACH every control on this page?
        ///
        /// A picture cannot answer that, and it is where the last two menu bugs
        /// have been: not a missing button but a button with nothing pointing at
        /// it. "Unable to access SLEEP with a controller when the shop is shut"
        /// is the shape of it — one control on the page turns non-interactable,
        /// the graph is rebuilt around the hole, and something below it is left
        /// with no arrow leading in.
        ///
        /// The graph tested is the GEOMETRIC one, because that is the one the
        /// player uses: MenuNavWatch swaps the creation-order chain out for it a
        /// frame after the page appears, and edit mode runs no LateUpdate, so it
        /// has to be applied here by hand exactly as the watchdog would.
        /// </summary>
        static void CheckNavReach(LifeHomeScreen screen, string where)
        {
            var body = Field<RectTransform>(screen, "body");
            var tabs = Field<System.Collections.Generic.List<UnityEngine.UI.Button>>(
                screen, "tabButtons");
            if (body == null) return;

            var rows = MenuNav.Collect(body);
            if (rows.Count == 0) return;
            var tabSel = tabs != null
                ? tabs.ConvertAll(b => (UnityEngine.UI.Selectable)b)
                : new System.Collections.Generic.List<UnityEngine.UI.Selectable>();

            if (!MenuNav.RectsResolved(rows))
            {
                Debug.Log("[HomePreview] " + where + " nav: rects unresolved, not checked");
                return;
            }
            MenuNav.Grid(rows);
            if (tabSel.Count > 0) MenuNav.JoinLines(tabSel, rows, null);

            // Flood fill from the tab bar, which is where a pad always starts.
            var all = new System.Collections.Generic.List<UnityEngine.UI.Selectable>(tabSel);
            all.AddRange(rows);
            var seen = new System.Collections.Generic.HashSet<UnityEngine.UI.Selectable>();
            var queue = new System.Collections.Generic.Queue<UnityEngine.UI.Selectable>();
            var start = tabSel.Count > 0 ? tabSel[0] : rows[0];
            seen.Add(start); queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var nav = queue.Dequeue().navigation;
                foreach (var next in new[] { nav.selectOnUp, nav.selectOnDown,
                                             nav.selectOnLeft, nav.selectOnRight })
                    if (next != null && seen.Add(next)) queue.Enqueue(next);
            }

            var lost = new System.Collections.Generic.List<string>();
            foreach (var s in all)
                if (s != null && !seen.Contains(s)) lost.Add(s.name);
            if (lost.Count > 0)
                Debug.LogError("[HomePreview] " + where + " nav: UNREACHABLE BY PAD — " +
                               string.Join(", ", lost));
            else
                Debug.Log("[HomePreview] " + where + " nav: all " + all.Count +
                          " controls reachable");
        }

        static T Field<T>(object obj, string name) where T : class =>
            obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
               ?.GetValue(obj) as T;

        static void SetField(object obj, string name, object value)
        {
            var f = obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) Debug.LogError("[HomePreview] no " + name + " field");
            else f.SetValue(obj, value);
        }
    }
}
