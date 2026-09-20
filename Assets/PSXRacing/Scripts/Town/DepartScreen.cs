using UnityEngine;
using UnityEngine.SceneManagement;
using PSXRacing.LifeSim;

namespace PSXRacing.Town
{
    /// <summary>
    /// The edge of a zone, and the only place the game asks where you are
    /// going.
    ///
    /// The doors: IN TOWN from your own street, HEAD HOME from the town's
    /// ends, GO RACING from either, INSPECT A CAR from home, MAKE THE DELIVERY
    /// first and highlighted when there is an order on the seat, and TURN
    /// BACK. Modelled on <see cref="OnFoot.StoreScreen"/> — same overlay
    /// canvas, same Escape handling, same pad wiring, same onClosed contract
    /// that hands the car back to whoever froze it.
    ///
    /// It is opened by DRIVING THROUGH THE ZONE LINE — <see cref="TownEdge"/>
    /// at the end of your street and at both ends of the town's — not by
    /// pressing at a junction volume; that volume is gone. The old worry was
    /// the toll booth: the only road out of your street is also the road into
    /// town, and a menu that opened every time you used it would be one you
    /// dismissed forty times a career. TownEdge's latch answers it — TURN BACK
    /// hands the car back and the line does not ask again until the car has
    /// genuinely driven away and returned — and the line itself is drawn
    /// across the road, so nothing here happens without warning.
    ///
    /// Every row but TURN BACK is a scene load now: the town and your street
    /// are separate maps, and the hop through the front end is where the
    /// drive gets banked.
    /// </summary>
    public class DepartScreen : MonoBehaviour
    {
        public System.Action onClosed;
        public CarController playerCar;

        /// <summary>Opened at one of the TOWN's ends rather than at the
        /// junction on your own street. The rows differ: from town the way
        /// out is HEAD HOME, and IN TOWN would be a row to where you are.</summary>
        public bool fromTown;

        public bool IsOpen { get; private set; }

        Canvas canvas;

        LifeState S => LifeSimManager.State;

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            Arrest();
            // The sound goes with the car. Every scene load brings it back,
            // and TURN BACK brings it back here — see AudioFader.
            AudioFader.FadeOut();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Build();
        }

        /// <summary>
        /// STOP THE CAR DEAD, here, on the line.
        ///
        /// Both of this screen's openers take the controls away — input off,
        /// handbrake on — and that is all they did. It is enough at the
        /// junction VOLUME, which will not claim a car over 4.5 km/h, and it is
        /// nowhere near enough at the junction LINE, which is crossed at
        /// whatever speed the player arrives at. Losing the input hands the car
        /// to PlayerCarInput's no-driver branch (brake 0.3, and the lever only
        /// once it is under 1 m/s), so a car doing 140 needs the better part of
        /// a hundred metres to come to rest and has twenty-two before the
        /// boundary wall. The note that used to sit on the opener called that
        /// "a wall met behind a menu" and shrugged; the player heard it hit,
        /// and watched it through the backdrop, which is 90% opaque and not
        /// 100%.
        ///
        /// Killing the velocity outright rather than braking harder: there is
        /// no braking figure that stops a car in twenty-two metres from every
        /// speed it can arrive at, and the honest reading of the moment is that
        /// the drive is OVER — the menu is up, the player is not driving, and
        /// the car is where they drove it to. The park hold in the solver keeps
        /// it there once it is stopped (atRest + handbrakeInput), which is why
        /// this can be a one-shot and does not need to fight gravity on a 12%
        /// street for as long as the menu is open.
        ///
        /// Safe on the stop-and-press path too, where the car is already inside
        /// the 4.5 km/h gate and this takes away a walking pace.
        /// </summary>
        void Arrest()
        {
            if (playerCar == null || playerCar.Body == null) return;
            playerCar.Body.linearVelocity = Vector3.zero;
            playerCar.Body.angularVelocity = Vector3.zero;
            // And tell the order on the back seat that this was not braking —
            // see PizzaCargo.ForgetMotion. Arriving at the junction WITH a
            // delivery aboard is not a corner case, it is the first row on the
            // panel this method is opening.
            PizzaCargo.Instance?.ForgetMotion();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (canvas != null) Destroy(canvas.gameObject);
            canvas = null;
            // TURN BACK: the drive goes on, and so does the sound.
            AudioFader.FadeIn();
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
        }

        void Build()
        {
            MenuKit.EnsureEventSystem();
            canvas = MenuKit.Canvas(transform, "DepartCanvas", 140);
            // GT2 charcoal + blueprint grid, matching the home screen: the
            // junction question is a menu and should read as one of the family.
            MenuKit.Panel(canvas.transform, "Backdrop", new Color(0.10f, 0.10f, 0.10f, 0.90f));
            MenuKit.GridBackdrop(canvas.transform);
            MenuKit.Scanlines(canvas.transform);

            var panel = MenuKit.Stretch(canvas.transform, "Depart",
                Vector2.zero, Vector2.one, 60f, 60f, 40f, -40f, MenuKit.PanelBg);

            MenuKit.Label(panel, "WHERE TO?", MenuKit.Title, new Vector2(0.5f, 1f),
                new Vector2(0f, -20f), TextAnchor.MiddleCenter, MenuKit.Accent, 700f, bold: true)
                .rectTransform.pivot = new Vector2(0.5f, 1f);

            var car = S.ActiveCar;
            float y = -80f;
            MenuKit.Label(panel,
                (car != null ? car.displayName.ToUpperInvariant() : "NO CAR") +
                "   ·   FUEL " + Mathf.RoundToInt(car != null ? car.fuel : 0f) + "%" +
                "   ·   " + LifeRules.SlotNames[Mathf.Clamp(S.slotIndex, 0, 2)],
                16, new Vector2(0.5f, 1f), new Vector2(0f, y), TextAnchor.MiddleCenter,
                MenuKit.Dim, 760f);
            y -= 48f;

            // The two that LEAVE need a car with something in it, the same
            // guard PizzaShift.Drive carries — a career that strands itself
            // with an empty tank on the far side of a scene load is a career
            // that cannot recover.
            bool canDrive = car != null && car.fuel > 5f;

            // An order on the seat rewrites the junction: the delivery is the
            // reason you drove out here, so it is the first door — and the
            // doors that would carry a hot pizza off to a race meeting or a
            // stranger's driveway are shut while you are holding it.
            bool carrying = PizzaRun.Carrying;

            // COUNT THE DOORS BEFORE DRAWING ONE. The budget used to be three
            // and the fourth row was simply never offered — GO RACING and
            // INSPECT were hidden while carrying partly for that reason. There
            // are four from your own street now (IN TOWN, CHARLOTTE, GO RACING,
            // INSPECT A CAR) because the house stopped offering them, so the
            // pitch has to answer to the count rather than the other way round.
            int doors = 1;                                   // the way on
            if (carrying) doors += 1;                        // MAKE THE DELIVERY
            else { doors += 2; if (!fromTown) doors += 1; }  // Charlotte, racing, the paper
            SetRowBudget(doors, y);

            if (carrying)
            {
                string venue = PizzaRun.TrackIndex >= 0 &&
                               PizzaRun.TrackIndex < TrackCatalog.All.Length
                    ? TrackCatalog.All[PizzaRun.TrackIndex].name : "the drop";
                Row(panel, ref y, "MAKE THE DELIVERY",
                    venue + "  ·  $" + PizzaRun.Pay + " on the door, more under " +
                    LifeRules.DeliveryClock(PizzaRun.ParSeconds) + ".",
                    canDrive, () => Leave("deliverrun"));
            }

            // A DRIVE, not a dismissal. This row used to Close, because the
            // town and your street were one map and "in town" meant "carry on
            // down this road". They are two maps now — the owner's ask, and the
            // reason the house you walk around and the house you drive past are
            // finally the same house — so the row that says IN TOWN has to take
            // you there.
            // WHICH WAY IS OUT depends on which line you are standing on. At
            // the junction on your own street the road out leads to town; at
            // either end of the town's street it leads home. The same screen
            // serves both, so the rows say where THIS line goes.
            if (fromTown)
                Row(panel, ref y, "HEAD HOME",
                    canDrive ? "Back up your own street. A few minutes down the road."
                             : "Not enough fuel to get there.",
                    canDrive, () => Leave("drivehome"));
            else
                // On a meet night the row says so: the lot is in town, and this
                // is the last place the player is asked where they are going.
                Row(panel, ref y, "IN TOWN",
                    !canDrive ? "Not enough fuel to get there."
                    : CarMeets.OnNow(S) ? "Car meet tonight at " + CarMeets.PlaceName.ToLowerInvariant() +
                                          " — east end of the main street."
                    : "The shop, the pumps, the lot and the yard. A few minutes down the road.",
                    canDrive, () => Leave("town"));

            // Hidden outright while carrying, not greyed: two shut doors
            // explain themselves less well than their absence beside MAKE THE
            // DELIVERY does, and a hot pizza has one errand.
            if (!carrying)
            {
                // CHARLOTTE. This was a button in the house — FREE ROAM — and
                // it is a destination now, because the house asks which car and
                // the line asks where. It is the one door here that does not
                // lead to a place you could have walked to; that is the point
                // of it.
                Row(panel, ref y, "FREE ROAM — CHARLOTTE",
                    canDrive ? "An hour out on the interstate. Nothing is scored."
                             : "Not enough fuel to go anywhere.",
                    canDrive, () => Leave("charlotte"));

                // THE RACE STARTS HERE. It used to drop the player on the home
                // screen holding the blurb below as an instruction — "set the
                // venue and the money at home, then drive out" — which is the
                // drive they had just made. The pre-race page opens on the
                // other side of this row instead, with START on it.
                // RACED TODAY IS A CLOSED DOOR, not an open one onto a refusal.
                // The pre-race page has always known about the one-purse-a-day
                // cap and greys its own START for it — but it was a page you
                // opened from the sofa. Reached from here the player has
                // already made the drive, so the answer has to be on this side
                // of it.
                var booked = LifeRules.BookingAt(S, S.day, S.slotIndex);
                bool racedToday = LifeRules.RacedToday(S);
                Row(panel, ref y, "GO RACING",
                    !canDrive ? "Not enough fuel to go anywhere."
                    : racedToday ? "One purse a day, and today's is won. Back tomorrow."
                    : booked != null
                        ? TrackCatalog.At(booked.trackIndex).name + "  ·  in the diary for " +
                          LifeRules.SlotNames[booked.slot].ToLowerInvariant() + "."
                        : "Pick a venue and go. Nothing is written in for this block.",
                    canDrive && !racedToday, () => Leave("racenow"));

                // The classifieds are a thing you read at home; from the
                // town's end the row would be a hop through the house to a
                // car on somebody else's street.
                if (!fromTown)
                {
                    int forSale = (S.newspaper != null ? S.newspaper.Count : 0);
                    Row(panel, ref y, "INSPECT A CAR",
                        forSale > 0
                            ? forSale + " in the paper this week. Pick one and drive over."
                            : "Nothing in the classifieds worth the drive today.",
                        canDrive && forSale > 0, () => Leave("market"));
                }
            }

            y -= 6f;
            MenuKit.Button(panel, "TURN BACK", new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(320f, 44f), Close, 17);

            // UGUI routes pad navigation to the SELECTED object and nothing
            // here would ever select one — a player on a pad could look at
            // three doors and walk through none of them.
            var rows = MenuNav.Collect(panel);
            MenuNav.Column(rows);
            if (rows.Count > 0)
            {
                MenuNav.Select(rows[0]);
                var watch = MenuNav.Watch(gameObject, rows[0]);
                MenuNav.Defer(watch, null, rows, null);
            }
        }

        /// <summary>How tall one door and its line are. Set by
        /// <see cref="SetRowBudget"/> before any of them are drawn.</summary>
        float rowPitch = RoomyPitch;

        /// <summary>The pitch a three-row panel has always used, and the
        /// ceiling: a door with room around it reads better than four crammed
        /// ones, so extra space is never spent widening the gaps.</summary>
        const float RoomyPitch = 78f;

        /// <summary>
        /// Fit the doors on the screen, whatever there are of them.
        ///
        /// The numbers, because they are easy to get wrong and impossible to
        /// see going wrong on a desktop: the handheld design column is 560
        /// units (MenuKit.DesignHeightHandheld) and this panel insets 40 top
        /// and bottom, so everything below the title and the status line shares
        /// about 350 — less TURN BACK's 50, which is not negotiable, because a
        /// page whose way out is off the bottom of a phone is a trap rather
        /// than a menu.
        ///
        /// Three rows still get exactly the 78 they had. Four get 70 and the
        /// difference comes off the blurb, not the button: the button is the
        /// thumb target.
        /// </summary>
        void SetRowBudget(int rows, float firstY) =>
            rowPitch = PitchFor(rows, firstY, MenuKit.DesignHeight);

        /// <summary>40 top + 40 bottom — see the Stretch call in Build.</summary>
        public const float PanelInset = 80f;

        /// <summary>TURN BACK and the gap above it. Not negotiable.</summary>
        public const float WayOut = 50f;

        /// <summary>And a little air under it. Without this the tightest case
        /// lands the way out flush with the panel's own border, which passes
        /// every check and looks like the page was cut off.</summary>
        public const float BottomMargin = 12f;

        /// <summary>The pure half of <see cref="SetRowBudget"/>, so the
        /// self-test can put a phone's 560-unit column in and check that four
        /// doors and the way out land above the bottom of it. Nothing else in
        /// this file can be checked without building a canvas, and the failure
        /// it guards against is invisible on a desktop.</summary>
        public static float PitchFor(int rows, float firstY, float designHeight)
        {
            float avail = (designHeight - PanelInset) - Mathf.Abs(firstY)
                          - WayOut - BottomMargin;
            return rows <= 0 ? RoomyPitch : Mathf.Clamp(avail / rows, 58f, RoomyPitch);
        }

        /// <summary>One door and one line about it, at whatever pitch
        /// <see cref="SetRowBudget"/> settled on.</summary>
        void Row(RectTransform panel, ref float y, string label, string blurb,
                 bool enabled, UnityEngine.Events.UnityAction go)
        {
            float btnH = Mathf.Clamp(rowPitch - 32f, 40f, 46f);
            float blurbH = rowPitch - btnH - 2f;
            MenuKit.Button(panel, label, new Vector2(0.5f, 1f), new Vector2(0f, y),
                new Vector2(460f, btnH), enabled ? go : null, 20,
                enabled ? (Color?)null : MenuKit.BtnBgDisabled);
            y -= btnH + 2f;
            MenuKit.Label(panel, blurb, 14, new Vector2(0.5f, 1f), new Vector2(0f, y),
                TextAnchor.MiddleCenter, MenuKit.Dim, 700f, height: blurbH);
            y -= blurbH;
        }

        /// <summary>
        /// Out of town and back to the front end.
        ///
        /// Banks the drive first, exactly like the pause menu's EXIT: free roam
        /// has no finish line, so leaving IS the finish and this is the only
        /// moment the LifeSim hears about the metres, the fuel and the paint.
        /// </summary>
        void Leave(string tab)
        {
            IsOpen = false;
            // Leaving for another zone THAT HAS A LINE OF ITS OWN arms the
            // arrival: the far side puts the car through it, rolling, rather
            // than on a driveway. Charlotte has no zone line and a race has no
            // zone at all, so neither gets one.
            bool crossing = tab == "town" || tab == "drivehome";
            TownEdge.ArrivePending = crossing;
            // WHETHER THE TRIP IS OVER is a different question from whether a
            // line is being crossed, and conflating them would charge a race
            // two blocks.
            //
            // A block is spent where a journey ENDS. Your street and the town
            // are two maps of one trip, so a crossing does not end it; neither
            // does driving out to Charlotte or out to a race, because the thing
            // at the far end — the free roam, the race — is what the block was
            // for and is what pays for it. What DOES end a trip is arriving
            // somewhere that is a menu: the classifieds put the player back in
            // the house with the evening gone, and that is the honest price of
            // having driven out to read them.
            //
            // (LifeHomeScreen.driveUnpaid is the other half: back out of the
            // pre-race page without starting and the drive is charged there,
            // so an unpaid leg cannot be ridden for free.)
            bool continues = crossing || tab == "charlotte" || tab == "racenow";
            // A CROSSING IS NOT THE END OF A DRIVE, so it does not cost what
            // the end of one costs. Your street and the town are two maps of
            // one trip: the hop between them banks the metres, the fuel and
            // the wear, and the BLOCK of the day is charged once, when the
            // trip actually ends — parked at home, or quit from the pause
            // menu. It used to be charged at every line, which made a run to
            // the shops and back cost the whole day, a shift cost two blocks
            // (the drive in, then the counter) — and, the case that found it,
            // rolled the clock over at the junction on the way to a car meet:
            // leave at night, arrive next morning, to an empty lot.
            TownExit.GoHome(playerCar, tab, commute: continues);
        }
    }
}
