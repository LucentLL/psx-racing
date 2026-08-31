using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// The widgets the advanced-tuning screen needs and MenuKit does not have: a
    /// stepper row, a padlocked row, and a gearing plot.
    ///
    /// The stepper is two buttons and a bar rather than a draggable slider, and
    /// that is a decision rather than a shortcut. A real slider would need an
    /// IMoveHandler, and MenuNav overwrites every Selectable's navigation
    /// unconditionally one frame later, so the drag would fight the pad. Two
    /// buttons fall straight into the existing navigation graph with no new
    /// code — and a disabled button is dropped from that graph entirely, which
    /// is exactly the behaviour a locked row wants.
    /// </summary>
    public static class SetupRow
    {
        public const float RowH = 40f;
        public const float RowStep = 62f;      // row + its end-label line
        /// <summary>Wide enough for the longest label at the enforced type
        /// floor. "TIRE PRESSURE FRONT" is 19 characters and ran straight under
        /// the '&lt;' button at 250 — measured off the first render, not guessed.
        /// </summary>
        public const float LabelW = 300f;
        public const float ArrowW = 44f;
        public const float TrackW = 250f;
        public const float ValueW = 170f;
        public const float Gap = 6f;

        /// <summary>Total width one row occupies, so the page can check it fits
        /// before it draws thirty of them.</summary>
        public static float Width =>
            LabelW + Gap + ArrowW + Gap + TrackW + Gap + ArrowW + Gap + ValueW;

        /// <summary>
        /// One adjustable row. Returns the two buttons so the page can name them
        /// — see the note in <see cref="Draw"/> about why that matters.
        /// </summary>
        /// <param name="asBuilt">Physical value to print instead of the one the
        /// slider position implies. Used by the gearing page, where the ratios
        /// are clamped into descending order after the fact — the row has to
        /// show the gearbox the car gets, not the one that was asked for.
        /// </param>
        public static void Draw(RectTransform parent, float colL, float y,
                                SetupParam p, CarSetupRange range, float t,
                                bool selected, System.Action<float> onChanged,
                                float? asBuilt = null)
        {
            float x = colL;

            MenuKit.Label(parent, CarSetupTable.Label(p), MenuKit.Body,
                new Vector2(0.5f, 1f), new Vector2(x, y), TextAnchor.MiddleLeft,
                selected ? MenuKit.Accent : Color.white, LabelW, RowH, bold: selected);
            x += LabelW + Gap;

            // --- the two steppers and the bar between them.
            // Everything below mutates ONLY this row's own value text and fill
            // rect. It deliberately does NOT call Rebuild(): every other handler
            // on this screen does, and Rebuild tears down and re-allocates the
            // entire page body — which is fine for a button press and absurd for
            // a value the player is going to nudge fifteen times in a row.
            var track = MenuKit.Rect(parent, "Track", new Vector2(0.5f, 1f),
                new Vector2(0f, 0.5f), new Vector2(x + ArrowW + Gap, y - RowH * 0.5f),
                new Vector2(TrackW, 14f), new Color(0f, 0f, 0f, 0.5f));

            // The fill grows from the notch at the factory setting, not from the
            // left end, so "where is this relative to stock" is readable at a
            // glance — which is the question a tuning screen is actually asked.
            float def01 = range.max - range.min > 1e-6f
                ? Mathf.Clamp01((range.def - range.min) / (range.max - range.min)) : 0.5f;
            var fill = MenuKit.Rect(track, "Fill", new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), Vector2.zero, new Vector2(1f, 10f), MenuKit.Accent);
            var notch = MenuKit.Rect(track, "Notch", new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(TrackW * def01, 0f),
                new Vector2(2f, 20f), new Color(1f, 1f, 1f, 0.45f));
            notch.SetAsLastSibling();

            var value = MenuKit.Label(parent, "", MenuKit.Body, new Vector2(0.5f, 1f),
                new Vector2(colL + Width, y), TextAnchor.MiddleRight,
                Color.white, ValueW, RowH);

            float cur = t;
            // Only the FIRST draw can show an as-built override — after that the
            // row is nudging its own value in place and has no way to re-run the
            // clamp, which depends on the neighbouring gears. So a nudge on the
            // gearing page falls back to the asked-for number until the page
            // rebuilds, and marks it so the difference is not a silent lie.
            bool overridden = asBuilt.HasValue;
            System.Action redraw = () =>
            {
                value.text = overridden
                    ? (asBuilt.Value * range.display).ToString("F" + range.decimals) +
                      (string.IsNullOrEmpty(range.unit) ? "" : " " + range.unit)
                    : range.Text(cur);
                overridden = false;
                float f01 = range.Fill01(cur);
                float a = Mathf.Min(def01, f01) * TrackW;
                float b = Mathf.Max(def01, f01) * TrackW;
                fill.anchoredPosition = new Vector2(a, 0f);
                fill.sizeDelta = new Vector2(Mathf.Max(2f, b - a), 10f);
            };
            redraw();

            // y is the TOP edge for both labels and buttons — MenuKit pivots
            // both on their anchor, and the anchor here is (0.5, 1). Passing a
            // half-row offset would drop the arrows below their own label.
            var dec = MenuKit.Button(parent, "<", new Vector2(0.5f, 1f),
                new Vector2(x, y), new Vector2(ArrowW, RowH),
                () => { cur = Mathf.Max(-1f, cur - CarSetupTable.Step); redraw(); onChanged(cur); },
                MenuKit.Body);
            x += ArrowW + Gap + TrackW + Gap;
            var inc = MenuKit.Button(parent, ">", new Vector2(0.5f, 1f),
                new Vector2(x, y), new Vector2(ArrowW, RowH),
                () => { cur = Mathf.Min(1f, cur + CarSetupTable.Step); redraw(); onChanged(cur); },
                MenuKit.Body);

            // MenuKit names a button after its caption, and thirty rows all
            // captioned "<" would be thirty GameObjects called "Btn_<". The nav
            // cursor is restored BY NAME across a rebuild, so without this the
            // cursor jumps to the first row on the page every time the screen
            // redraws. Name them after the parameter instead.
            dec.gameObject.name = "Btn_" + p + "_dec";
            inc.gameObject.name = "Btn_" + p + "_inc";

            // Pivot the arrow buttons on their own left edge like everything
            // else on this screen; MenuKit.Button pivots on the anchor, which is
            // centred, so nudge them back by half.
            Recentre(dec.GetComponent<RectTransform>(), ArrowW);
            Recentre(inc.GetComponent<RectTransform>(), ArrowW);

            DrawEnds(parent, colL, y - RowH, p, range);
        }

        /// <summary>
        /// A row the car has not earned yet. Drawn in the SAME position as the
        /// adjustable version — the reference screens keep row order stable
        /// whatever the car is fitted with, and a row that vanishes teaches the
        /// player nothing.
        ///
        /// The button is built with a null handler, which MenuKit renders
        /// non-interactable and MenuNav then drops from the pad graph entirely.
        /// So the cursor steps straight over a locked row for free.
        /// </summary>
        /// <param name="value">What the car is sitting at RIGHT NOW, already
        /// formatted. Optional, and the reason it exists: a part changes the
        /// car whether or not it unlocked a slider, and a padlocked RIDE
        /// HEIGHT row on a car with lowering springs fitted has to be able to
        /// say 270 mm. Without it the screen reads as the part not being
        /// fitted — which is exactly how it was reported.</param>
        public static void DrawLocked(RectTransform parent, float colL, float y,
                                      SetupParam p, string reason, string value = null)
        {
            MenuKit.Label(parent, "[-] " + CarSetupTable.Label(p), MenuKit.Body,
                new Vector2(0.5f, 1f), new Vector2(colL, y), TextAnchor.MiddleLeft,
                MenuKit.Dim, LabelW + ArrowW + Gap * 2f, RowH);

            // Left-aligned where the slider track would start, so it cannot
            // collide with the reason: the reason is right-aligned and grows
            // LEFTWARD from the far edge, and the longest of them ("NEEDS
            // CLOSE-RATIO GEAR SET") still stops well short of here.
            if (!string.IsNullOrEmpty(value))
                MenuKit.Label(parent, value, MenuKit.Small,
                    new Vector2(0.5f, 1f),
                    new Vector2(colL + LabelW + Gap + ArrowW + Gap, y),
                    TextAnchor.MiddleLeft, MenuKit.Dim, 170f, RowH);

            // No padlock glyph: the menu font is LegacyRuntime.ttf and anything
            // it does not have renders as a box. "[-]" extends the "#"/"-" the
            // parts page already uses for fitted / not fitted.
            MenuKit.Label(parent, reason, MenuKit.Body,
                new Vector2(0.5f, 1f), new Vector2(colL + Width, y),
                TextAnchor.MiddleRight, MenuKit.Bad,
                TrackW + ValueW + ArrowW, RowH);
        }

        static void Recentre(RectTransform rt, float w)
        {
            if (rt != null) rt.anchoredPosition += new Vector2(w * 0.5f, 0f);
        }

        /// <summary>The two end captions under the bar — "Acceleration ...
        /// Speed", "Soft ... Stiff". They are what make a bare number mean
        /// something, and they are why the reference screens are readable.
        /// </summary>
        static void DrawEnds(RectTransform parent, float colL, float y,
                             SetupParam p, CarSetupRange range)
        {
            CarSetupTable.EndLabels(p, out string low, out string high);
            float trackX = colL + LabelW + Gap + ArrowW + Gap;
            MenuKit.Label(parent, low, MenuKit.Small, new Vector2(0.5f, 1f),
                new Vector2(trackX, y), TextAnchor.MiddleLeft, MenuKit.Dim, 140f, 20f);
            MenuKit.Label(parent, high, MenuKit.Small, new Vector2(0.5f, 1f),
                new Vector2(trackX + TrackW, y), TextAnchor.MiddleRight,
                MenuKit.Dim, 140f, 20f);
        }
    }

    /// <summary>
    /// The RPM-against-speed plot on the gearing page: one line per gear, from
    /// idle to redline, so the ratios can be read as a shape rather than as six
    /// numbers.
    ///
    /// The line and dot primitives are copied from CarXray rather than shared
    /// with it. That is deliberate: CarXray's are private helpers inside a
    /// car-specific draw, and making them public would couple the inspection
    /// X-ray — the more delicate of the two screens — to a tuning plot. Forty
    /// lines of duplication is the cheaper of the two prices.
    /// </summary>
    public static class SetupGraph
    {
        public static void Draw(RectTransform parent, float colL, float y,
                                float w, float h, in CarSetupBasis basis,
                                float[] ratios, float finalDrive)
        {
            var panel = MenuKit.Rect(parent, "GearGraph", new Vector2(0.5f, 1f),
                new Vector2(0f, 1f), new Vector2(colL, y), new Vector2(w, h),
                new Color(0f, 0f, 0f, 0.5f));
            if (ratios == null || ratios.Length == 0 || basis.wheelRadius < 0.01f) return;

            const float padL = 46f, padR = 12f, padT = 12f, padB = 26f;
            float plotW = w - padL - padR, plotH = h - padT - padB;
            if (plotW < 20f || plotH < 20f) return;

            float redline = Mathf.Max(1000f, basis.redlineRPM);
            // A little past the car's own top speed, so the top gear's line has
            // somewhere to go instead of stopping dead on the frame edge.
            float vMaxKmh = Mathf.Max(40f, basis.topSpeedMps * 3.6f * 1.15f);

            Vector2 At(float kmh, float rpm) => new Vector2(
                padL + Mathf.Clamp01(kmh / vMaxKmh) * plotW,
                -padT - (1f - Mathf.Clamp01(rpm / redline)) * plotH);

            // Redline rule and the spec'd top speed, so the lines have something
            // to be read against.
            Line(panel, At(0f, redline), At(vMaxKmh, redline), new Color(1f, 0.4f, 0.36f, 0.5f));
            Line(panel, At(basis.topSpeedMps * 3.6f, 0f), At(basis.topSpeedMps * 3.6f, redline),
                 new Color(1f, 1f, 1f, 0.22f));

            for (int g = 0; g < ratios.Length; g++)
            {
                float total = ratios[g] * finalDrive;
                if (total < 1e-3f) continue;
                // speed(rpm) = rpm / (ratio*FD) * circumference / 60
                float kmhAtRedline = redline / total * (2f * Mathf.PI * basis.wheelRadius)
                                     / 60f * 3.6f;
                var c = g == 0 ? MenuKit.Accent
                              : new Color(0.72f, 0.74f, 0.85f, 0.85f - g * 0.06f);
                Line(panel, At(0f, 0f), At(kmhAtRedline, redline), c);
            }

            MenuKit.Label(panel, Mathf.RoundToInt(redline) + " rpm", MenuKit.Small,
                new Vector2(0f, 1f), new Vector2(4f, -padT + 2f), TextAnchor.UpperLeft,
                MenuKit.Dim, 90f, 20f);
            MenuKit.Label(panel, Mathf.RoundToInt(vMaxKmh) + " km/h", MenuKit.Small,
                new Vector2(1f, 0f), new Vector2(-4f, 4f), TextAnchor.LowerRight,
                MenuKit.Dim, 110f, 20f);
        }

        /// <summary>A line as a rotated 1-px Image. UGUI has no line primitive
        /// and a Canvas full of them is still cheaper than a mesh.</summary>
        static void Line(RectTransform parent, Vector2 a, Vector2 b, Color c)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f) return;
            var go = new GameObject("Line");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = a;
            rt.sizeDelta = new Vector2(len, 2f);
            rt.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }
    }
}
