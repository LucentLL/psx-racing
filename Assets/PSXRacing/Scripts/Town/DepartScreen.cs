using UnityEngine;
using UnityEngine.SceneManagement;
using PSXRacing.LifeSim;

namespace PSXRacing.Town
{
    /// <summary>
    /// The end of your street, and the only place the game asks where you are
    /// going.
    ///
    /// Three doors: IN TOWN, GO RACING, INSPECT A CAR. Modelled on
    /// <see cref="OnFoot.StoreScreen"/> — same overlay canvas, same Escape
    /// handling, same pad wiring, same onClosed contract that hands the car
    /// back to whoever froze it.
    ///
    /// It is reached by PRESSING at the junction rather than by driving over a
    /// line. That is the whole difference between a signpost and a toll booth:
    /// the only road out of your street is also the road into town, and a menu
    /// that opened every time you used it would be a menu you dismissed forty
    /// times a career.
    ///
    /// STAY IN TOWN is not a scene load. The town already contains the shop,
    /// the forecourt, the lot and the yard — closing the panel is arriving.
    /// </summary>
    public class DepartScreen : MonoBehaviour
    {
        public System.Action onClosed;
        public CarController playerCar;

        public bool IsOpen { get; private set; }

        Canvas canvas;

        LifeState S => LifeSimManager.State;

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
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
        }

        void Build()
        {
            MenuKit.EnsureEventSystem();
            canvas = MenuKit.Canvas(transform, "DepartCanvas", 140);
            MenuKit.Panel(canvas.transform, "Backdrop", new Color(0.03f, 0.03f, 0.05f, 0.88f));
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

            Row(panel, ref y, "IN TOWN",
                "The shop, the pumps, the lot and the yard are all down this road.",
                true, Close);

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

                int forSale = (S.newspaper != null ? S.newspaper.Count : 0);
                Row(panel, ref y, "INSPECT A CAR",
                    forSale > 0
                        ? forSale + " in the paper this week. Pick one and drive over."
                        : "Nothing in the classifieds worth the drive today.",
                    canDrive && forSale > 0, () => Leave("market"));
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
            TownExit.GoHome(playerCar, tab);
        }
    }
}
