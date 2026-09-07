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
                Row(panel, ref y, "IN TOWN",
                    canDrive ? "The shop, the pumps, the lot and the yard. A few minutes down the road."
                             : "Not enough fuel to get there.",
                    canDrive, () => Leave("town"));

            // Hidden outright while carrying, not greyed: the panel's row
            // budget is three (see Row), and a fourth pushes the way out off
            // the bottom of a phone. Two shut doors explain themselves less
            // well than their absence beside MAKE THE DELIVERY does.
            if (!carrying)
            {
                Row(panel, ref y, "GO RACING",
                    canDrive ? "Set the venue and the money at home, then drive out."
                             : "Not enough fuel to go anywhere.",
                    canDrive, () => Leave("main"));

                // The classifieds are a thing you read at home; from the
                // town's end the row would be a hop through the house to a
                // car on somebody else's street, and the budget is three.
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

        /// <summary>
        /// One door and one line about it. The budget is tight and worth
        /// stating: the handheld design column is 560 units and this panel
        /// insets 40 top and bottom, so three rows plus a title, a status line
        /// and a way out have 480 units between them. At 78 per row it fits
        /// with 30 to spare; at 86 it did not, and a page that scrolls is a
        /// page whose last door is off the bottom of a phone.
        /// </summary>
        void Row(RectTransform panel, ref float y, string label, string blurb,
                 bool enabled, UnityEngine.Events.UnityAction go)
        {
            MenuKit.Button(panel, label, new Vector2(0.5f, 1f), new Vector2(0f, y),
                new Vector2(460f, 46f), enabled ? go : null, 20,
                enabled ? (Color?)null : MenuKit.BtnBgDisabled);
            y -= 48f;
            MenuKit.Label(panel, blurb, 14, new Vector2(0.5f, 1f), new Vector2(0f, y),
                TextAnchor.MiddleCenter, MenuKit.Dim, 700f, height: 26f);
            y -= 30f;
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
            // Leaving for another DRIVABLE zone arms the arrival: the far
            // side puts the car through its own line, rolling, rather than on
            // a driveway. A page (racing, the classifieds) is not a zone and
            // gets nothing.
            TownEdge.ArrivePending = tab == "town" || tab == "drivehome";
            TownExit.GoHome(playerCar, tab);
        }
    }
}
