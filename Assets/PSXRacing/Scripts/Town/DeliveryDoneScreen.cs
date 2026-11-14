using UnityEngine;
using UnityEngine.UI;
using PSXRacing.LifeSim;

namespace PSXRacing.Town
{
    /// <summary>
    /// THE DOOR OPENS: what a drop in the neighbourhood was worth.
    ///
    /// A delivery used to end on a race track's results screen. It ends on a
    /// doorstep now (TownWorld.Deliver), so the same four facts the race's
    /// result line carried are said here, on one panel, while the player is
    /// still standing at the door: how long it took against the quote, what
    /// state the order arrived in, the tip that was handed over, and the wages
    /// the shift is earning on the weekly cheque. The money has already moved
    /// when this opens (LifeRules.SettleDelivery); the panel only reports it.
    ///
    /// Modelled on <see cref="DepartScreen"/>: the same overlay canvas and
    /// colours, one button, pad-selectable. Fixed rows on a fixed panel that
    /// fits the handheld column (560 units) with room to spare, so nothing in
    /// it can clip or ride over another line at any aspect.
    /// </summary>
    public class DeliveryDoneScreen : MonoBehaviour
    {
        public static DeliveryDoneScreen Open { get; private set; }

        /// <summary>The panel's size in design units: inside 560 tall with
        /// the margins DepartScreen keeps, inside a 4:3 column's 746 wide.</summary>
        public const float PanelW = 640f, PanelH = 392f;

        Canvas canvas;
        bool built;

        public string address = "", order = "";
        public LifeRules.DeliveryOutcome outcome;
        public int boxes = 1;
        public int wagesThisWeek;
        public string note = "";

        public static DeliveryDoneScreen Show(string address, string order, LifeRules.DeliveryOutcome o,
                                              int boxes, int wagesThisWeek, string note)
        {
            if (Open != null) Destroy(Open.gameObject);
            var go = new GameObject("DeliveryDone");
            var s = go.AddComponent<DeliveryDoneScreen>();
            s.address = address ?? "";
            s.order = order ?? "";
            s.outcome = o;
            s.boxes = boxes;
            s.wagesThisWeek = wagesThisWeek;
            s.note = note ?? "";
            s.Build();
            return s;
        }

        void Awake() { Open = this; }
        void OnDestroy() { if (Open == this) Open = null; }

        public void Close()
        {
            if (canvas != null) Destroy(canvas.gameObject);
            canvas = null;
            Destroy(gameObject);
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) ||
                (pad != null && pad.buttonEast.wasPressedThisFrame))
                Close();
        }

        /// <summary>The rows as words, pure, so the self-test and the play
        /// check read the same strings the panel draws.</summary>
        public static void Rows(LifeRules.DeliveryOutcome o, int boxes, int wages,
                                out string time, out string state, out string tip, out string pay)
        {
            time = LifeRules.DeliveryClock(o.seconds) + "    PAR " + LifeRules.DeliveryClock(o.parSeconds);
            string label = LifeRules.OrderLabel(o, boxes);
            state = string.IsNullOrEmpty(label) ? "INTACT" : label;
            tip = o.refused ? "$0 — REFUSED" : "+" + MenuKit.Money(o.tip);
            pay = MenuKit.Money(Mathf.Max(0, wages)) + " SO FAR THIS WEEK";
        }

        void Build()
        {
            if (built) return;
            built = true;
            MenuKit.EnsureEventSystem();
            canvas = MenuKit.Canvas(transform, "DeliveryDoneCanvas", 150);
            MenuKit.Panel(canvas.transform, "Backdrop", new Color(0.10f, 0.10f, 0.10f, 0.55f));

            var panel = MenuKit.Stretch(canvas.transform, "DeliveryDone",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                -PanelW * 0.5f, -PanelW * 0.5f, -PanelH * 0.5f, PanelH * 0.5f, MenuKit.PanelBg);

            var top = new Vector2(0.5f, 1f);
            bool refused = outcome.refused;
            MenuKit.Label(panel, refused ? "REFUSED AT THE DOOR" : "DELIVERED", 40, top,
                new Vector2(0f, -16f), TextAnchor.MiddleCenter, refused ? MenuKit.Bad : MenuKit.Accent,
                PanelW - 40f, height: 52f, bold: true);
            MenuKit.Label(panel, Clip(address + (string.IsNullOrEmpty(order) ? "" : "   ·   " + order), 56), 19, top,
                new Vector2(0f, -72f), TextAnchor.MiddleCenter, MenuKit.Dim, PanelW - 40f, height: 28f);

            Rows(outcome, boxes, wagesThisWeek, out string time, out string state, out string tip, out string pay);
            float y = -116f;
            Row(panel, ref y, "TIME", time, Color.white);
            Row(panel, ref y, "THE ORDER", Clip(state, 26), Color.white);
            Row(panel, ref y, "TIP", tip, refused ? MenuKit.Bad : MenuKit.Good);
            Row(panel, ref y, "WAGES", pay, Color.white);

            if (!string.IsNullOrEmpty(note))
                MenuKit.Label(panel, Clip(note, 64), 16, top, new Vector2(0f, y - 4f),
                    TextAnchor.MiddleCenter, MenuKit.Dim, PanelW - 40f, height: 24f);

            var carryOn = MenuKit.Button(panel, "CARRY ON", new Vector2(0.5f, 0f),
                new Vector2(0f, 46f), new Vector2(300f, 46f), Close, 20);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            var rows = MenuNav.Collect(panel);
            MenuNav.Column(rows);
            if (rows.Count > 0) MenuNav.Select(rows[0]);
            else if (carryOn != null) carryOn.Select();
        }

        /// <summary>One fact: its name on the left, the figure on the right,
        /// on a line of its own.</summary>
        static void Row(RectTransform panel, ref float y, string name, string value, Color col)
        {
            var top = new Vector2(0.5f, 1f);
            MenuKit.Label(panel, name, 19, top, new Vector2(-PanelW * 0.5f + 40f, y),
                TextAnchor.MiddleLeft, MenuKit.Dim, 200f, height: 34f);
            MenuKit.Label(panel, value, 22, top, new Vector2(PanelW * 0.5f - 40f, y),
                TextAnchor.MiddleRight, col, 340f, height: 34f, bold: true);
            y -= 42f;
        }

        static string Clip(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }
}
