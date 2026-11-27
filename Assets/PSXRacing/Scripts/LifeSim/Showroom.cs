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
    /// (never made up), and in a band the figures a buyer reads first - the
    /// price or value, a drivetrain diagram drawn from rects (FF / FR / MR /
    /// RR / 4WD, nose up), year, power, weight - with the page's own actions.
    /// The band is a column beside the stage on a wide screen and a row under
    /// it on a 4:3 one, and all of it - name, stage, band, buttons - fits the
    /// first screen (BandBeside).
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

        /// <summary>The year in a catalog-style name's tail ('98 -> 1998), 0 if none.</summary>
        public static int YearOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            string n = name.Trim();
            if (n.Length < 3) return 0;
            string last = n.Substring(n.Length - 3);
            if ((last[0] != '`' && last[0] != '\'' && last[0] != '’') || !char.IsDigit(last[1]) || !char.IsDigit(last[2]))
                return 0;
            int yy = (last[1] - '0') * 10 + (last[2] - '0');
            return yy >= 30 ? 1900 + yy : 2000 + yy;
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
            if (c.year <= 0) c.year = YearOf(name);
            string make = MakeOf(name);
            c.makeYear = make + (c.year > 0 ? "   ·   " + c.year : "");
            if (string.IsNullOrEmpty(c.model)) c.model = name ?? "";
            return c;
        }

        // ------------------------------------------------------------------
        //  The block
        // ------------------------------------------------------------------

        /// <summary>The rows over the stage: the name (64) and the tag / make /
        /// count line under it (30).</summary>
        public const float NameH = 64f + 30f;
        /// <summary>From the stage to the band: under it, beside it.</summary>
        const float UnderGap = 12f, SideGap = 22f;
        const float MaxUnderStage = 400f;

        /// <summary>The smallest stage. Below it a page scrolls instead of the
        /// car shrinking to a sliver (only a phone-sized page gets that far).</summary>
        static float MinStage => MenuKit.DesignHeight < 700f ? 140f : 160f;

        /// <summary>
        /// Where the band stands (owner's window, 2026-11-15: at 2000x1077 the
        /// band under a full-width stage ran off the bottom of the scroll
        /// pages). On a wide canvas (1.6:1 and up) it is a column BESIDE the
        /// stage, on the right - the money, the figures, then the buttons -
        /// and the stage takes the rest of the width and all the height under
        /// the name, which there is also the bigger car. A block that is tall
        /// for its width (4:3, NEW GAME's 700-unit panel on 16:9) keeps the
        /// band UNDER a full-width stage: there that is the bigger car.
        /// </summary>
        public static bool BandBeside(float width, float room)
        {
            float aspect = MenuKit.HalfWidth * 2f / Mathf.Max(1f, MenuKit.DesignHeight);
            return aspect >= 1.6f && width >= 720f && width >= room * 1.75f;
        }

        /// <summary>The column beside the stage: wide enough for the price, the
        /// figures and the buttons' words; the car keeps the rest.</summary>
        static float SideW(float width) => Mathf.Clamp(width * 0.24f, 270f, 300f);

        /// <summary>
        /// Build the showroom into <paramref name="parent"/> (anchors at its
        /// top centre, like every page body here), from <paramref name="top"/>
        /// down, between <paramref name="left"/> and left + width.
        /// <paramref name="room"/> is the height from <paramref name="top"/> to
        /// the bottom of the first screen: the name rows, the stage and the
        /// whole band with its buttons are fitted into it, the stage as big as
        /// that allows; a page only has to scroll to the band when even the
        /// smallest stage does not fit. The car must already be on the viewer
        /// (Show / ShowOwned). Returns the height used.
        /// </summary>
        /// <param name="browse">-1 / +1 to step to the previous / next car;
        /// null (or count under 2) for a page about one car.</param>
        public static float Build(RectTransform parent, CarViewer viewer, Card card, float left, float width,
                                  float top, float room, int index, int count, System.Action<int> browse)
        {
            if (count < 2) browse = null;
            int heroFirst = parent.childCount;
            bool acts = card.actions != null && card.actions.Count > 0;
            bool beside = BandBeside(width, room);
            float bandW = beside ? SideW(width) : 0f;
            float stageW = beside ? width - bandW - SideGap : width;
            float stageTop = top - NameH;

            NameRows(parent, card, left, stageW, top, index, count, browse);

            // The band first, where its height can be measured; the stage takes
            // what that leaves of the room.
            int bandFirst = parent.childCount;
            var buttons = new List<Selectable>();
            float stageH, bottom;
            if (beside)
            {
                // The column, from the name's line down: the money, the figures,
                // and the buttons standing on the stage's bottom line.
                float bx = left + stageW + SideGap, y = top;
                PriceBlock(parent, card, bx, bandW, ref y);
                if (y < top) y -= 8f;
                Cluster(parent, card, bx, bandW, ref y);
                float stack = acts ? StackH(card.actions.Count) : 0f;
                int actFirst = parent.childCount;
                if (acts)
                {
                    float ya = 0f;
                    Actions(parent, card.actions, bx, bandW, ref ya, buttons);
                }
                float heroH = Mathf.Max(room, NameH + MinStage, (top - y) + (acts ? 10f + stack : 0f));
                stageH = heroH - NameH;
                bottom = top - heroH;
                if (acts) Shift(parent, actFirst, bottom + stack);
            }
            else
            {
                float bandH = Band(parent, card, left, width, buttons);
                stageH = Mathf.Clamp(room - NameH - UnderGap - bandH, MinStage, MaxUnderStage);
                float bandTop = stageTop - stageH - UnderGap;
                Shift(parent, bandFirst, bandTop);
                bottom = bandTop - bandH;
            }

            var stage = Stage(parent, viewer, card, left, stageW, stageTop, stageH, browse, out var prev, out var next);
            // Before the band in the page's order, as on screen: the cursor
            // starts on the arrows and DOWN goes on to the buttons.
            stage.SetSiblingIndex(bandFirst);
            var block = stage.gameObject.AddComponent<ShowroomBlock>();
            block.prev = prev;
            block.next = next;
            block.beside = beside;
            block.acts = buttons.ToArray();
            var parts = new List<RectTransform>();
            for (int i = heroFirst; i < parent.childCount; i++)
                if (parent.GetChild(i) is RectTransform rt) parts.Add(rt);
            block.parts = parts.ToArray();
            return top - bottom;
        }

        /// <summary>Move what was built at y 0, from child <paramref name="from"/>
        /// on, down to <paramref name="y"/>.</summary>
        static void Shift(RectTransform parent, int from, float y)
        {
            for (int i = from; i < parent.childCount; i++)
                if (parent.GetChild(i) is RectTransform rt) rt.anchoredPosition += new Vector2(0f, y);
        }

        /// <summary>The band under the stage, built at y 0: three columns, the
        /// money, the figures, the page's actions (the figures take the room
        /// when it has none). Returns its height.</summary>
        static float Band(RectTransform parent, Card card, float left, float width, List<Selectable> made)
        {
            const float Gap = 22f;
            bool acts = card.actions != null && card.actions.Count > 0;
            float w1 = acts ? width * 0.35f : width * 0.42f;
            float w2 = acts ? width * 0.30f : width - w1 - Gap;
            float x2 = left + w1 + Gap, x3 = x2 + w2 + Gap, w3 = left + width - x3;
            float y1 = 0f, y2 = 0f, y3 = 0f;
            // (nothing to price or say: the figures start at the left)
            if (!acts && string.IsNullOrEmpty(card.price) && string.IsNullOrEmpty(card.note)) { x2 = left; w2 = width; }
            PriceBlock(parent, card, left, w1, ref y1);
            Cluster(parent, card, x2, w2, ref y2);
            if (acts) Actions(parent, card.actions, x3, w3, ref y3, made);
            return -Mathf.Min(y1, Mathf.Min(y2, y3));
        }

        /// <summary>The name, big and italic, over the stage; under it what the
        /// car is to this page (left), the make and year and the count (right).</summary>
        static void NameRows(RectTransform parent, Card card, float left, float width, float top,
                             int index, int count, System.Action<int> browse)
        {
            bool browsing = browse != null;
            var anchor = new Vector2(0.5f, 1f);
            float y = top;
            bool handheld = MenuKit.DesignHeight < 700f;

            // A sideways swipe over the name browses (the stage itself turns the car).
            if (browsing)
            {
                var catcher = MenuKit.Rect(parent, "ShowroomSwipe", anchor, new Vector2(0f, 1f),
                    new Vector2(left, y), new Vector2(width, NameH), new Color(0f, 0f, 0f, 0f));
                catcher.gameObject.AddComponent<ShowroomSwipe>().browse = browse;
            }

            // The name: as big as fits the width, bold italic, centred.
            int size = handheld ? 38 : 44;
            var title = MenuKit.Label(parent, card.model, size, anchor, new Vector2(left + width * 0.5f, y),
                TextAnchor.MiddleCenter, Color.white, width, height: 58f, bold: true);
            title.fontStyle = FontStyle.BoldAndItalic;
            while (size > 24 && title.preferredWidth > width - 24f)
            {
                size -= 2;
                title.fontSize = size;
            }
            if (title.preferredWidth > width - 24f) MenuKit.FitOneLine(title, width - 24f);
            y -= 64f;   // (a clear gap: an italic's descenders and slant reach past its rect)

            // Under it: what the car is to this page on the left (NEW, the
            // lane, where it stands), the make and year and the count on the right.
            float cw = 0f;
            if (browsing)
            {
                var ct = MenuKit.Label(parent, (index + 1) + " / " + count, 18, anchor, new Vector2(left + width, y),
                    TextAnchor.MiddleRight, MenuKit.Dim, 110f, height: 26f, bold: true);
                cw = Mathf.Min(110f, ct.preferredWidth) + 24f;
            }
            var mk = MenuKit.Label(parent, card.makeYear, 18, anchor, new Vector2(left + width - cw, y),
                TextAnchor.MiddleRight, MenuKit.Accent, width * 0.5f, height: 26f, bold: true);
            float mw = Mathf.Min(width * 0.5f, mk.preferredWidth) + cw + 24f;
            if (!string.IsNullOrEmpty(card.tag))
            {
                var tag = MenuKit.Label(parent, card.tag, 18, anchor, new Vector2(left, y), TextAnchor.MiddleLeft,
                    card.tagColor, width - mw, height: 26f, bold: true);
                if (tag.preferredWidth > width - mw) MenuKit.FitOneLine(tag, width - mw);
            }
        }

        /// <summary>The stage: the studio frame, its rules and words, and the
        /// &lt; &gt; arrows when the page browses.</summary>
        static RectTransform Stage(RectTransform parent, CarViewer viewer, Card card, float left, float width,
                                   float y, float stageH, System.Action<int> browse,
                                   out Selectable prev, out Selectable next)
        {
            prev = next = null;
            var stage = MenuKit.Rect(parent, "ShowroomStage", new Vector2(0.5f, 1f), new Vector2(0f, 1f),
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
            MenuKit.Label(stage, "DRAG TO TURN", 14, new Vector2(0f, 1f), new Vector2(16f, -6f), TextAnchor.MiddleLeft,
                new Color(0.50f, 0.50f, 0.53f), width * 0.3f, height: 26f).raycastTarget = false;
            if (!string.IsNullOrEmpty(card.paint))
                MenuKit.Label(stage, card.paint, 18, new Vector2(1f, 0f), new Vector2(-18f, 36f),
                    TextAnchor.MiddleRight, new Color(0.86f, 0.86f, 0.88f), width * 0.4f, height: 28f, bold: true)
                    .raycastTarget = false;
            if (browse != null)
            {
                float aw = 60f, ah = Mathf.Min(104f, stageH * 0.42f);
                var p = MenuKit.Button(stage, "<", new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(aw, ah),
                    () => browse(-1), 32, new Color(0.10f, 0.10f, 0.11f, 0.92f));
                var n = MenuKit.Button(stage, ">", new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(aw, ah),
                    () => browse(1), 32, new Color(0.10f, 0.10f, 0.11f, 0.92f));
                p.gameObject.AddComponent<ShowroomBrowseKeys>().browse = browse;
                n.gameObject.AddComponent<ShowroomBrowseKeys>().browse = browse;
                prev = p;
                next = n;
            }
            // carparts: OPEN on the stage (bottom left) for a car whose shell
            // has parts; ShowroomParts shows it only then, and reads O and the
            // wheel's OPEN as well as a click, a tap or the pad's A on it
            if (viewer != null && CarParts.On)
            {
                var sp = stage.gameObject.AddComponent<ShowroomParts>();
                sp.viewer = viewer;
                sp.button = MenuKit.Button(stage, "OPEN HOOD", new Vector2(0f, 0f), new Vector2(16f, 16f), new Vector2(200f, 46f),
                    sp.Step, 17, new Color(0.10f, 0.10f, 0.11f, 0.92f));
                sp.button.gameObject.SetActive(CarParts.Allowed(viewer.Shown, viewer.BodyRenderer != null ? viewer.BodyRenderer.GetComponent<MeshFilter>() : null));
            }
            return stage;
        }

        /// <summary>
        /// The showroom's pad links on top of a page's geometric graph (see
        /// <see cref="ShowroomBlock"/>): now, over the creation-order chain the
        /// page has just installed, and again whenever the watch puts in the
        /// graph it builds from the resolved rects. Called right after a page's
        /// MenuNav.Defer; a page without a showroom is left alone.
        /// </summary>
        public static void AfterGrid(MenuNavWatch watch, RectTransform root)
        {
            if (root == null || root.GetComponentInChildren<ShowroomBlock>(false) == null) return;
            ApplyLinks(root);
            if (watch == null) return;
            var prior = watch.applied;
            watch.applied = () => { prior?.Invoke(); if (root != null) ApplyLinks(root); };
        }

        static void ApplyLinks(RectTransform root)
        {
            foreach (var b in root.GetComponentsInChildren<ShowroomBlock>(false)) b.Link();
        }

        static void PriceBlock(RectTransform parent, Card card, float x, float w, ref float y)
        {
            var anchor = new Vector2(0.5f, 1f);
            if (!string.IsNullOrEmpty(card.price))
            {
                MenuKit.Label(parent, card.priceLabel, 16, anchor, new Vector2(x, y), TextAnchor.MiddleLeft,
                    MenuKit.Dim, w, height: 22f, bold: true);
                var v = MenuKit.Label(parent, card.price, 30, anchor, new Vector2(x, y - 22f), TextAnchor.MiddleLeft,
                    card.priceColor, w, height: 38f, bold: true);
                int s = 30;
                while (s > 20 && v.preferredWidth > w) { s -= 2; v.fontSize = s; }
                if (v.preferredWidth > w) MenuKit.FitOneLine(v, w);
                y -= 62f;
            }
            if (!string.IsNullOrEmpty(card.note))
            {
                MenuKit.Para(parent, card.note, 16, anchor, new Vector2(x, y), out float h,
                    TextAnchor.UpperLeft, card.noteColor, w);
                y -= h + 6f;
            }
        }

        /// <summary>The drivetrain diagram and year / power / weight.</summary>
        static void Cluster(RectTransform parent, Card card, float x, float w, ref float y)
        {
            var anchor = new Vector2(0.5f, 1f);
            const float BoxW = 52f, BoxH = 90f;
            float top = y - 2f;
            bool drawn = DrivetrainDiagram(parent, card.drv, x, top, BoxW, BoxH);
            float tx = drawn ? x + BoxW + 16f : x, tw = x + w - tx;
            float ly = top;
            void Line(string text, bool bold, string shorter = null)
            {
                MenuKit.Rect(parent, "Dot", anchor, new Vector2(0f, 0.5f), new Vector2(tx, ly - 11f), new Vector2(8f, 8f),
                    MenuKit.Accent).GetComponent<Image>().raycastTarget = false;
                var l = MenuKit.Label(parent, text, 17, anchor, new Vector2(tx + 16f, ly), TextAnchor.MiddleLeft,
                    Color.white, tw - 16f, height: 22f, bold: bold);
                // the short form rather than a cut one (no text may clip)
                if (l.preferredWidth > tw - 16f && shorter != null) l.text = shorter;
                if (l.preferredWidth > tw - 16f) MenuKit.FitOneLine(l, tw - 16f);
                ly -= 23f;
            }
            if (!string.IsNullOrEmpty(card.drv)) Line(card.drv + "  ·  " + DrvWords(card.drv), true, card.drv);
            if (card.year > 0) Line(card.year.ToString(), false);
            if (card.hp > 0) Line(card.hp + " hp", false);
            if (card.kg > 0) Line(card.kg.ToString("N0") + " kg", false);
            y = Mathf.Min(drawn ? top - BoxH : top, ly) - 8f;
        }

        public static string DrvWords(string drv)
        {
            switch (drv)
            {
                case "FF": return "FRONT DRIVE";
                case "FR": return "REAR DRIVE";
                case "MR": return "MID-ENGINE";
                case "RR": return "REAR-ENGINE";
                case "4WD": return "ALL-WHEEL DRIVE";
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

        const float ActH = 50f, ActGap = 8f;

        /// <summary>A stack of <paramref name="n"/> buttons from its y to the
        /// last one's bottom edge.</summary>
        static float StackH(int n) => n > 0 ? 2f + n * ActH + (n - 1) * ActGap : 0f;

        /// <summary>The page's own buttons, one under another.</summary>
        static void Actions(RectTransform parent, List<Action> actions, float x, float w, ref float y,
                            List<Selectable> made)
        {
            for (int i = 0; i < actions.Count; i++)
            {
                var a = actions[i];
                var b = MenuKit.Button(parent, a.label, new Vector2(0.5f, 1f),
                    new Vector2(MenuKit.ColLeft(x, w), y - 2f), new Vector2(w, ActH),
                    a.onClick, 17, a.onClick == null ? MenuKit.BtnBgDisabled : a.bg);
                var t = b.GetComponentInChildren<Text>();
                if (t != null && t.preferredWidth > w - 12f) MenuKit.FitOneLine(t, w - 12f);
                if (made != null) made.Add(b);
                y -= ActH + ActGap;
            }
            y -= 4f;
        }

    }

    /// <summary>
    /// On the stage: what the showroom block is made of (its parts, for the
    /// preview's first-screen check) and its pad links, kept on top of the
    /// page's geometric graph (Showroom.AfterGrid). DOWN from the &lt; &gt; arrows
    /// goes to the band's first button wherever the band stands - beside the
    /// stage it is in no column of the arrows' - UP from that button comes
    /// back to the &gt; arrow, and LEFT from a button beside the stage goes to
    /// it too. LEFT / RIGHT on the arrows still browse (ShowroomBrowseKeys).
    /// </summary>
    public class ShowroomBlock : MonoBehaviour
    {
        public Selectable prev, next;
        public Selectable[] acts;
        public RectTransform[] parts;
        public bool beside;

        /// <summary>The band's first button the pad can land on, or null.</summary>
        public Selectable FirstAct()
        {
            if (acts != null)
                foreach (var a in acts)
                    if (a != null && a.IsInteractable()) return a;
            return null;
        }

        public void Link()
        {
            var first = FirstAct();
            Selectable last = null;
            if (acts != null)
                foreach (var a in acts)
                    if (a != null && a.IsInteractable()) last = a;
            // Out of the block, DOWN: to the page's first line under it, the
            // control nearest across (a narrow stage's arrow is in no column of
            // the page's own buttons, and the geometric graph then fell through
            // to a row far down the page).
            foreach (var from in new[] { last ?? prev, last != null ? null : next })
            {
                var under = Below(from);
                if (under == null) continue;
                var n = Explicit(from);
                n.selectOnDown = under;
                from.navigation = n;
            }
            if (first == null) return;
            foreach (var arrow in new[] { prev, next })
            {
                if (arrow == null) continue;
                var n = Explicit(arrow);
                n.selectOnDown = first;
                arrow.navigation = n;
            }
            Selectable back = next != null ? next : prev;
            if (back == null) return;
            var f = Explicit(first);
            f.selectOnUp = back;
            first.navigation = f;
            if (!beside) return;
            foreach (var a in acts)
            {
                if (a == null || !a.IsInteractable()) continue;
                var n = Explicit(a);
                n.selectOnLeft = back;
                a.navigation = n;
            }
        }

        /// <summary>The control on the page's first line under the block that
        /// is nearest <paramref name="from"/> across, or null.</summary>
        Selectable Below(Selectable from)
        {
            var page = transform.parent as RectTransform;
            if (from == null || page == null || parts == null) return null;
            var c = new Vector3[4];
            float bottom = float.MaxValue;
            foreach (var p in parts)
            {
                if (p == null) continue;
                p.GetWorldCorners(c);
                bottom = Mathf.Min(bottom, page.InverseTransformPoint(c[0]).y);
            }
            ((RectTransform)from.transform).GetWorldCorners(c);
            float fx = page.InverseTransformPoint((c[0] + c[2]) * 0.5f).x;
            var under = new List<(Selectable s, float top, float dx)>();
            float topMost = float.MinValue;
            foreach (var s in page.GetComponentsInChildren<Selectable>(false))
            {
                if (s == null || !s.IsInteractable() || Mine(s)) continue;
                ((RectTransform)s.transform).GetWorldCorners(c);
                Vector3 lo = page.InverseTransformPoint(c[0]), hi = page.InverseTransformPoint(c[2]);
                if (hi.y > bottom + 1f) continue;
                under.Add((s, hi.y, Mathf.Abs((lo.x + hi.x) * 0.5f - fx)));
                topMost = Mathf.Max(topMost, hi.y);
            }
            Selectable best = null;
            float bestDx = float.MaxValue;
            foreach (var u in under)
                if (u.top >= topMost - 20f && u.dx < bestDx) { bestDx = u.dx; best = u.s; }
            return best;
        }

        bool Mine(Selectable s)
        {
            foreach (var p in parts)
                if (p != null && s.transform.IsChildOf(p)) return true;
            return false;
        }

        static Navigation Explicit(Selectable s)
        {
            var n = s.navigation;
            n.mode = Navigation.Mode.Explicit;
            return n;
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

    /// <summary>
    /// carparts: the showroom's OPEN (decision 5: the showroom is a place parts
    /// open). One button on the stage steps the car on it SHUT, HOOD, TRUNK,
    /// DOORS, ALL; keyboard O and the wheel's OPEN do the same, and holding
    /// either shuts everything. Hidden for a car without parts (or with the LAB
    /// rows off); the stage's car is a turntable shell, so no speed rule and
    /// no sound (it stands far below the listener).
    /// </summary>
    public class ShowroomParts : MonoBehaviour
    {
        public CarViewer viewer;
        public Button button;
        float downAt = -1f;
        bool shutFired;

        MeshFilter Body => viewer != null && viewer.BodyRenderer != null ? viewer.BodyRenderer.GetComponent<MeshFilter>() : null;

        public void Step()
        {
            var def = viewer != null ? viewer.Shown : null;
            CarParts.Cycle(Body, def, player: false, doorsAllowed: true);
        }

        void Update()
        {
            if (button == null || viewer == null) return;
            var def = viewer.Shown;
            var body = Body;
            bool show = CarParts.Allowed(def, body);
            if (button.gameObject.activeSelf != show) button.gameObject.SetActive(show);
            if (!show) return;
            CarEngineBay.Preload(body, def);   // the showroom is a place the hood opens
            int avail = CarParts.Available(def), next = CarParts.NextStep(CarParts.MaskOf(body), avail, true);
            var t = button.GetComponentInChildren<Text>();
            string label = next == 0 ? "SHUT ALL" : "OPEN " + CarParts.StepName(next, avail);
            if (t != null && t.text != label) t.text = label;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            bool held = (kb != null && kb.oKey.isPressed) || (WheelInput.Active && WheelInput.OpenHeld);
            float now = Time.unscaledTime;
            if (held)
            {
                if (downAt < 0f) { downAt = now; shutFired = false; }
                else if (!shutFired && now - downAt >= CarParts.HoldToShutSeconds)
                {
                    shutFired = true;
                    CarParts.Request(body, def, 0);
                }
            }
            else if (downAt >= 0f)
            {
                if (!shutFired) Step();
                downAt = -1f;
            }
        }
    }
}
