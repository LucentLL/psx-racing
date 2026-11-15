using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// THE SHOWROOM (owner, 2026-11-15: "I want car viewing and car selection
    /// to look like the early Gran Turismo games, with the car rotating (or
    /// able to be rotated by player)", over a GT2 garage screen).
    ///
    /// One car at a time, big, turning on a dark stage (CarViewer's studio
    /// frame), the model's name over it in big bold italic, make and year
    /// under that, the paint's name under the car when the livery has one
    /// (never made up), and under the stage the figures a buyer reads first -
    /// the price or value, a drivetrain diagram drawn from rects (FF / FR / MR /
    /// RR / 4WD, nose up), year, power, weight - beside the page's own actions.
    /// The same KIND of screen as the reference in the game's own palette
    /// (MenuKit): no borrowed art, icons, fonts or symbols.
    ///
    /// Where a page lists cars, the stage shows one and LEFT / RIGHT browse:
    /// the d-pad or arrow keys on the stage's own &lt; &gt; buttons (they keep the
    /// cursor and step the car instead of moving off), a click or tap on them,
    /// or a sideways swipe over the name. LB / RB and PageUp / PageDown still
    /// switch the tabs; the right stick and , . turn the car.
    ///
    /// Switch: PSX_SHOWROOM=0 builds every page the way it was before.
    /// </summary>
    public static class Showroom
    {
        public static bool On = System.Environment.GetEnvironmentVariable("PSX_SHOWROOM") != "0";

        public struct Action
        {
            public string label;
            public UnityEngine.Events.UnityAction onClick;   // null = shown greyed
            public Color? bg;
            public Action(string label, UnityEngine.Events.UnityAction onClick, Color? bg = null)
            {
                this.label = label; this.onClick = onClick; this.bg = bg;
            }
        }

        /// <summary>What the stage says about the car on it.</summary>
        public class Card
        {
            public string tag;                 // small, over the name (NEW / the lane / where it is)
            public Color tagColor = MenuKit.Accent;
            public string model;               // the big italic name
            public string makeYear;            // MAKE  ·  1998
            public string paint;               // the livery's colour name, or null
            public string drv;                 // FF FR MR RR 4WD
            public int year, hp, kg;           // hp in horsepower; 0 = not known
            public string priceLabel = "PRICE";
            public string price;               // "$22,000" / "$68/mo x 48"
            public Color priceColor = Color.white;
            public string note;                // one or two lines under the price
            public Color noteColor = MenuKit.Dim;
            public List<Action> actions = new List<Action>();
        }

        // ------------------------------------------------------------------
        //  The card from the catalog
        // ------------------------------------------------------------------

        /// <summary>The catalog's name without the make in front or the year
        /// at the back ("Mazda RX-7 Type RS (FD) '98" -> "RX-7 Type RS (FD)").</summary>
        public static string ModelOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string n = name.Trim();
            int sp = n.LastIndexOf(' ');
            if (sp > 0)
            {
                string last = n.Substring(sp + 1);
                if (last.Length == 3 && (last[0] == '`' || last[0] == '\'' || last[0] == '’') &&
                    char.IsDigit(last[1]) && char.IsDigit(last[2]))
                    n = n.Substring(0, sp).TrimEnd();
            }
            int cut = n.IndexOf(' ');
            return cut > 0 && cut < n.Length - 1 ? n.Substring(cut + 1).Trim() : n;
        }

        public static string MakeOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string n = name.Trim();
            int cut = n.IndexOf(' ');
            return (cut > 0 ? n.Substring(0, cut) : n).ToUpperInvariant();
        }

        static readonly string[] ColourWords =
        {
            "red", "blue", "white", "black", "silver", "grey", "gray", "yellow", "green", "orange", "purple",
            "pink", "gold", "brown", "beige", "maroon", "navy", "teal", "cyan", "violet", "bronze", "copper",
            "champagne", "cream", "wine", "burgundy", "lime", "midnight", "pearl", "metallic", "dark", "light",
        };

        /// <summary>The livery's colour name, from the colour words in its
        /// baked name (the arcade's rule), or null when it has none - the stage
        /// then prints nothing rather than inventing one.</summary>
        public static string PaintName(CarModelDef def, int skin)
        {
            if (def == null || def.skinNames == null || skin < 0 || skin >= def.skinNames.Length) return null;
            string raw = def.skinNames[skin];
            if (string.IsNullOrEmpty(raw)) return null;
            var words = new List<string>();
            foreach (var w in raw.ToLowerInvariant().Split('_', '-', ' ', '.'))
                if (System.Array.IndexOf(ColourWords, w) >= 0) words.Add(w == "gray" ? "grey" : w);
            if (words.Count == 0) return null;
            // Only a colour: "metallic", "dark" or "light" alone is not one.
            bool colour = false;
            foreach (var w in words) if (w != "metallic" && w != "dark" && w != "light") colour = true;
            return colour ? string.Join(" ", words).ToUpperInvariant() : null;
        }

        /// <summary>A card filled from the catalog row (the page adds its tag,
        /// price, note and actions).</summary>
        public static Card From(CarSpec spec, string displayName, CarModelDef def, int skin)
        {
            string name = spec != null && !string.IsNullOrEmpty(spec.name) ? spec.name : displayName;
            var c = new Card
            {
                model = ModelOf(name),
                drv = spec != null ? spec.drv : null,
                year = spec != null ? spec.modelYear : 0,
                hp = spec != null ? CarSpec.ToHp(spec.hp) : 0,
                kg = spec != null ? spec.kg : 0,
                paint = PaintName(def, skin),
            };
            string make = MakeOf(name);
            c.makeYear = make + (c.year > 0 ? "   ·   " + c.year : "");
            if (string.IsNullOrEmpty(c.model)) c.model = name ?? "";
            return c;
        }

        // ------------------------------------------------------------------
        //  The block
        // ------------------------------------------------------------------

        /// <summary>The stage's height for a page: about half the room the
        /// page has, never a sliver and never more than a screen's worth.</summary>
        public static float StageHeight(float room) => Mathf.Clamp(room * 0.52f, 190f, 400f);

        /// <summary>
        /// Build the showroom into <paramref name="parent"/> (anchors at its
        /// top centre, like every page body here), from <paramref name="top"/>
        /// down, between <paramref name="left"/> and left + width. The car must
        /// already be on the viewer (Show / ShowOwned). Returns the height used.
        /// </summary>
        /// <param name="browse">-1 / +1 to step to the previous / next car;
        /// null (or count under 2) for a page about one car.</param>
        public static float Build(RectTransform parent, CarViewer viewer, Card card, float left, float width,
                                  float top, float stageH, int index, int count, System.Action<int> browse)
        {
            bool browsing = browse != null && count > 1;
            var anchor = new Vector2(0.5f, 1f);
            float y = top;
            bool handheld = MenuKit.DesignHeight < 700f;

            // A sideways swipe over the name browses (the stage itself turns the car).
            if (browsing)
            {
                var catcher = MenuKit.Rect(parent, "ShowroomSwipe", anchor, new Vector2(0f, 1f),
                    new Vector2(left, y), new Vector2(width, 28f + 50f + 30f), new Color(0f, 0f, 0f, 0f));
                catcher.gameObject.AddComponent<ShowroomSwipe>().browse = browse;
            }

            // The tag and the counter.
            if (!string.IsNullOrEmpty(card.tag))
            {
                var tag = MenuKit.Label(parent, card.tag, 18, anchor, new Vector2(left, y), TextAnchor.MiddleLeft,
                    card.tagColor, width * (browsing ? 0.74f : 1f), height: 26f, bold: true);
                MenuKit.FitOneLine(tag, width * (browsing ? 0.74f : 1f));
            }
            if (browsing)
                MenuKit.Label(parent, (index + 1) + " / " + count, 18, anchor, new Vector2(left + width, y),
                    TextAnchor.MiddleRight, MenuKit.Dim, width * 0.24f, height: 26f, bold: true);
            y -= 28f;

            // The name: as big as fits the width, bold italic, centred.
            int size = handheld ? 38 : 44;
            var title = MenuKit.Label(parent, card.model, size, anchor, new Vector2(left + width * 0.5f, y),
                TextAnchor.MiddleCenter, Color.white, width, height: 50f, bold: true);
            title.fontStyle = FontStyle.BoldAndItalic;
            while (size > 24 && title.preferredWidth > width - 24f)
            {
                size -= 2;
                title.fontSize = size;
            }
            if (title.preferredWidth > width - 24f) MenuKit.FitOneLine(title, width - 24f);
            y -= 50f;
            MenuKit.Label(parent, card.makeYear, 18, anchor, new Vector2(left + width * 0.5f, y),
                TextAnchor.MiddleCenter, MenuKit.Accent, width, height: 26f, bold: true);
            y -= 30f;

            // The stage.
            var stage = MenuKit.Rect(parent, "ShowroomStage", anchor, new Vector2(0f, 1f),
                new Vector2(left, y), new Vector2(width, stageH), new Color(0.016f, 0.016f, 0.02f, 1f));
            if (viewer != null) viewer.AttachStudio(stage);
            // A hairline at the top and bottom, the menus' own rule.
            foreach (float edge in new[] { 0f, 1f })
            {
                var line = MenuKit.Stretch(stage, "Rule", new Vector2(0f, edge), new Vector2(1f, edge), 0f, 0f,
                    edge > 0.5f ? -2f : 0f, edge > 0.5f ? 0f : 2f, MenuKit.Line);
                var img = line.GetComponent<Image>();
                if (img != null) img.raycastTarget = false;
            }
            if (!string.IsNullOrEmpty(card.paint))
            {
                var p = MenuKit.Label(stage, card.paint, 18, new Vector2(1f, 0f), new Vector2(-18f, 40f),
                    TextAnchor.MiddleRight, new Color(0.86f, 0.86f, 0.88f), width * 0.5f, height: 30f, bold: true);
                p.raycastTarget = false;
            }
            MenuKit.Label(stage, browsing ? "DRAG TO TURN  ·  < > FOR THE NEXT CAR" : "DRAG TO TURN", 14,
                new Vector2(0f, 0f), new Vector2(18f, 40f), TextAnchor.MiddleLeft, new Color(0.55f, 0.55f, 0.58f),
                width * 0.46f, height: 30f).raycastTarget = false;
            if (browsing)
            {
                float aw = 60f, ah = Mathf.Min(104f, stageH * 0.42f);
                var prev = MenuKit.Button(stage, "<", new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(aw, ah),
                    () => browse(-1), 32, new Color(0.10f, 0.10f, 0.11f, 0.92f));
                var next = MenuKit.Button(stage, ">", new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(aw, ah),
                    () => browse(1), 32, new Color(0.10f, 0.10f, 0.11f, 0.92f));
                // MenuKit.Button pivots at its anchor: put the middle on the line.
                foreach (var b in new[] { prev, next })
                {
                    var rt = (RectTransform)b.transform;
                    rt.pivot = new Vector2(rt.pivot.x, 0.5f);
                    b.gameObject.AddComponent<ShowroomBrowseKeys>().browse = browse;
                }
            }
            y -= stageH + 14f;

            // Under the stage: the money and the figures on the left, the
            // page's actions on the right (or the figures there when it has none).
            float gap = 24f;
            float lw = (width - gap) * 0.5f, rx = left + lw + gap, rw = width - lw - gap;
            bool acts = card.actions != null && card.actions.Count > 0;
            float yl = y, yr = y;
            PriceBlock(parent, card, left, lw, ref yl);
            if (acts)
            {
                Cluster(parent, card, left, lw, ref yl);
                Actions(parent, card.actions, rx, rw, ref yr);
            }
            else Cluster(parent, card, rx, rw, ref yr);
            return top - Mathf.Min(yl, yr);
        }

        static void PriceBlock(RectTransform parent, Card card, float x, float w, ref float y)
        {
            var anchor = new Vector2(0.5f, 1f);
            if (!string.IsNullOrEmpty(card.price))
            {
                float kw = Mathf.Min(150f, w * 0.36f);
                MenuKit.Label(parent, card.priceLabel, 18, anchor, new Vector2(x, y - 4f), TextAnchor.MiddleLeft,
                    MenuKit.Dim, kw, height: 36f, bold: true);
                var v = MenuKit.Label(parent, card.price, 30, anchor, new Vector2(x + kw, y - 2f), TextAnchor.MiddleLeft,
                    card.priceColor, w - kw, height: 40f, bold: true);
                MenuKit.FitOneLine(v, w - kw);
                y -= 44f;
            }
            if (!string.IsNullOrEmpty(card.note))
            {
                MenuKit.Para(parent, card.note, 16, anchor, new Vector2(x, y - 2f), out float h,
                    TextAnchor.UpperLeft, card.noteColor, w);
                y -= h + 8f;
            }
        }

        /// <summary>The drivetrain diagram and year / power / weight.</summary>
        static void Cluster(RectTransform parent, Card card, float x, float w, ref float y)
        {
            var anchor = new Vector2(0.5f, 1f);
            const float BoxW = 54f, BoxH = 88f;
            float top = y - 4f;
            bool drawn = DrivetrainDiagram(parent, card.drv, x + 2f, top, BoxW, BoxH);
            float tx = drawn ? x + BoxW + 18f : x, tw = x + w - tx;
            float ly = top - 2f;
            void Line(string text, Color c, bool bold)
            {
                MenuKit.Rect(parent, "Dot", anchor, new Vector2(0f, 0.5f), new Vector2(tx, ly - 11f), new Vector2(8f, 8f),
                    MenuKit.Accent).GetComponent<Image>().raycastTarget = false;
                var l = MenuKit.Label(parent, text, 17, anchor, new Vector2(tx + 16f, ly), TextAnchor.MiddleLeft,
                    c, tw - 16f, height: 22f, bold: bold);
                MenuKit.FitOneLine(l, tw - 16f);
                ly -= 23f;
            }
            if (!string.IsNullOrEmpty(card.drv)) Line(card.drv + "  ·  " + DrvWords(card.drv), Color.white, true);
            if (card.year > 0) Line(card.year.ToString(), Color.white, false);
            if (card.hp > 0) Line(card.hp + " hp", Color.white, false);
            if (card.kg > 0) Line(card.kg.ToString("N0") + " kg  ·  " + Mathf.RoundToInt(card.kg * 2.2046f).ToString("N0") + " lb",
                Color.white, false);
            y = Mathf.Min(drawn ? top - BoxH : top, ly) - 8f;
        }

        public static string DrvWords(string drv)
        {
            switch (drv)
            {
                case "FF": return "FRONT ENGINE, FRONT DRIVE";
                case "FR": return "FRONT ENGINE, REAR DRIVE";
                case "MR": return "MID ENGINE, REAR DRIVE";
                case "RR": return "REAR ENGINE, REAR DRIVE";
                case "4WD": return "FOUR-WHEEL DRIVE";
                default: return "";
            }
        }

        /// <summary>
        /// The car from above, nose up: the body's outline, four wheels (the
        /// driven ones lit), the driven axles and shafts, and the engine where
        /// it sits. Drawn from plain rects in the menu's own colours.
        /// </summary>
        static bool DrivetrainDiagram(RectTransform parent, string drv, float x, float top, float w, float h)
        {
            if (drv != "FF" && drv != "FR" && drv != "MR" && drv != "RR" && drv != "4WD") return false;
            var box = MenuKit.Rect(parent, "Drivetrain_" + drv, new Vector2(0.5f, 1f), new Vector2(0f, 1f),
                new Vector2(x, top), new Vector2(w, h), new Color(0f, 0f, 0f, 0.35f));
            box.GetComponent<Image>().raycastTarget = false;
            Color line = MenuKit.Accent, lit = MenuKit.Active, idle = new Color(0.36f, 0.36f, 0.38f);
            void R(float cx, float cy, float rw, float rh, Color c)
            {
                // centre-based, in the box (origin top-left, y down)
                var r = MenuKit.Rect(box, "p", new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                    new Vector2(cx, -cy), new Vector2(rw, rh), c);
                r.GetComponent<Image>().raycastTarget = false;
            }
            // the body
            R(w * 0.5f, 1f, w, 2f, line);
            R(w * 0.5f, h - 1f, w, 2f, line);
            R(1f, h * 0.5f, 2f, h, line);
            R(w - 1f, h * 0.5f, 2f, h, line);
            float fy = h * 0.2f, ry = h * 0.8f;
            bool front = drv == "FF" || drv == "4WD", rear = drv != "FF";
            // axles, then the wheels over their ends
            if (front) R(w * 0.5f, fy, w - 10f, 3f, lit);
            if (rear) R(w * 0.5f, ry, w - 10f, 3f, lit);
            foreach (float wy in new[] { fy, ry })
            {
                bool on = wy == fy ? front : rear;
                R(4f, wy, 9f, 18f, on ? lit : idle);
                R(w - 4f, wy, 9f, 18f, on ? lit : idle);
            }
            // the engine, and the shaft from it to a driven axle it is not on
            float ey = drv == "MR" ? h * 0.62f : drv == "RR" ? h * 0.86f : h * 0.30f;
            if (drv == "FR") R(w * 0.5f, (ey + ry) * 0.5f, 3f, ry - ey, lit);
            if (drv == "4WD") R(w * 0.5f, (fy + ry) * 0.5f, 3f, ry - fy, lit);
            R(w * 0.5f, ey, w * 0.42f, h * 0.17f, new Color(0.88f, 0.88f, 0.90f));
            return true;
        }

        static void Actions(RectTransform parent, List<Action> actions, float x, float w, ref float y)
        {
            int perRow = actions.Count <= 1 ? 1 : w >= 600f && actions.Count >= 3 ? 3 : 2;
            const float Gap = 10f, H = 52f;
            float bw = (w - Gap * (perRow - 1)) / perRow;
            for (int i = 0; i < actions.Count; i++)
            {
                int col = i % perRow;
                if (i > 0 && col == 0) y -= H + Gap;
                var a = actions[i];
                var b = MenuKit.Button(parent, a.label, new Vector2(0.5f, 1f),
                    new Vector2(MenuKit.ColLeft(x + col * (bw + Gap), bw), y - 4f), new Vector2(bw, H),
                    a.onClick, 17, a.onClick == null ? MenuKit.BtnBgDisabled : a.bg);
                var t = b.GetComponentInChildren<Text>();
                if (t != null) MenuKit.FitOneLine(t, bw - 12f);
            }
            y -= H + Gap + 4f;
        }
    }

    /// <summary>
    /// On the stage's &lt; &gt; buttons: LEFT / RIGHT (d-pad, arrow keys) step the
    /// car instead of moving the cursor off. Runs after the button's own
    /// Selectable.OnMove (components answer in the order they were added), so
    /// the cursor is put back on this button in the same frame - the
    /// MenuNavScroll pattern. UP / DOWN are the page's as usual.
    /// </summary>
    public class ShowroomBrowseKeys : MonoBehaviour, IMoveHandler
    {
        public System.Action<int> browse;

        public void OnMove(AxisEventData e)
        {
            if (e == null || browse == null) return;
            if (e.moveDir != MoveDirection.Left && e.moveDir != MoveDirection.Right) return;
            if (e.selectedObject != gameObject) e.selectedObject = gameObject;
            e.Use();
            browse(e.moveDir == MoveDirection.Right ? 1 : -1);
        }
    }

    /// <summary>
    /// A sideways swipe over the car's name browses (touch, or a mouse drag);
    /// an upward or downward one is handed to the page's scroll, as the stage
    /// does with its drags (CarViewerDrag).
    /// </summary>
    public class ShowroomSwipe : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public System.Action<int> browse;
        ScrollRect scroll;
        bool toScroll;

        void Awake() => scroll = GetComponentInParent<ScrollRect>();

        public void OnBeginDrag(PointerEventData e)
        {
            Vector2 travel = e.position - e.pressPosition;
            toScroll = Mathf.Abs(travel.y) > Mathf.Abs(travel.x);
            if (toScroll && scroll != null) scroll.OnBeginDrag(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (toScroll && scroll != null) scroll.OnDrag(e);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (toScroll) { if (scroll != null) scroll.OnEndDrag(e); toScroll = false; return; }
            float dx = (e.position - e.pressPosition).x;
            // A tenth of the screen, in pixels: the same flick on any device.
            if (browse != null && Mathf.Abs(dx) > Screen.width * 0.06f) browse(dx < 0f ? 1 : -1);
        }
    }
}
