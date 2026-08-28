using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// The X-ray plan view: this car's actual running gear, laid out the way
    /// the car actually is.
    ///
    /// Ported from RG2's `src/render/carBody/xrayDrivetrain.ts` — the layout
    /// formulas, the engine-block dimensions and the per-drivetrain branches
    /// are that file's, constant for constant. That work went through several
    /// rounds of "the driveshaft is off-centre", "why are the cylinders
    /// colliding with the block", "the engine is too far back on trucks", and
    /// re-deriving it here would be re-earning every one of those corrections.
    ///
    /// What changed in the port:
    ///
    /// - **Car units are METRES.** RG2 works in sprite units of about 40-60 per
    ///   car, so several of its sizes carry a `max(1.6, L*0.055)` floor for very
    ///   small sprites. At a 4.3 m length those floors would BE the whole car,
    ///   so only the proportional term survives.
    /// - **Canvas strokes become rects.** A UGUI Image cannot draw a line, so a
    ///   shaft is a thin RectTransform rotated onto its own angle. That is why
    ///   there is a Line() at all.
    /// - **The measurements come from the shell the car is wearing** — the same
    ///   CarModelDef the garage turntable and the physics rig use — so an FD and
    ///   a Charger really do get different wheelbases and tracks here.
    /// </summary>
    public static class CarXray
    {
        // ------------------------------------------------------------------
        //  Engine identity
        // ------------------------------------------------------------------
        public struct EngineShape
        {
            public string kind;     // inline / vee / flat / rotary
            public int perBank;
            public int banks;
        }

        /// <summary>
        /// GT4's engine-type string to a block shape. Deliberately a hand
        /// parser rather than a regex: the data has one "Rotar2" typo that the
        /// source special-cases, and a parser that reads the leading letters
        /// and digits handles it without a second pattern.
        /// </summary>
        public static EngineShape ShapeOf(string eType)
        {
            string s = (eType ?? "").ToUpperInvariant();
            int i = 0;
            while (i < s.Length && !char.IsDigit(s[i])) i++;
            string word = s.Substring(0, i);
            int n = 0;
            while (i < s.Length && char.IsDigit(s[i])) { n = n * 10 + (s[i] - '0'); i++; }

            if (n > 0)
            {
                if (word == "L" || word == "I") return Inline(n);
                if (word == "V") return new EngineShape { kind = "vee", perBank = Mathf.Max(1, Mathf.RoundToInt(n / 2f)), banks = 2 };
                if (word == "BOXER" || word == "F") return new EngineShape { kind = "flat", perBank = Mathf.Max(1, Mathf.RoundToInt(n / 2f)), banks = 2 };
                if (word == "ROTAR" || word == "ROTOR") return new EngineShape { kind = "rotary", perBank = Mathf.Max(1, n), banks = 1 };
            }
            return Inline(4);
        }

        static EngineShape Inline(int n) =>
            new EngineShape { kind = "inline", perBank = Mathf.Max(1, n), banks = 1 };

        /// <summary>Block footprint: length along the crank, width across it.</summary>
        public static Vector2 EngineDims(EngineShape shape, float L, float W)
        {
            switch (shape.kind)
            {
                case "vee": return new Vector2(L * (0.085f + 0.022f * shape.perBank), W * 0.34f);
                case "flat": return new Vector2(L * (0.075f + 0.024f * shape.perBank), W * 0.52f);
                case "rotary": return new Vector2(L * (0.055f + 0.032f * shape.perBank), W * 0.24f);
                default: return new Vector2(L * (0.075f + 0.019f * shape.perBank), W * 0.21f);
            }
        }

        // ------------------------------------------------------------------
        //  The pen
        // ------------------------------------------------------------------
        /// <summary>
        /// Draws in CAR-LOCAL metres: +X forward (the nose points RIGHT, as in
        /// the source's plan view), +Y to the car's left. One scale for both
        /// axes so the car keeps its proportions.
        /// </summary>
        class Pen
        {
            public RectTransform panel;
            public float scale;

            RectTransform Make(string name, Vector2 centre, Vector2 size, float angleDeg, Color c)
            {
                var go = new GameObject(name);
                go.transform.SetParent(panel, false);
                var img = go.AddComponent<Image>();
                img.color = c;
                img.raycastTarget = false;
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = centre * scale;
                rt.sizeDelta = size * scale;
                if (Mathf.Abs(angleDeg) > 0.01f) rt.localRotation = Quaternion.Euler(0f, 0f, angleDeg);
                return rt;
            }

            /// <summary>Axis-aligned block: len along the car, wid across it.</summary>
            public void Box(float cx, float cy, float len, float wid, Color c) =>
                Make("box", new Vector2(cx, cy), new Vector2(len, wid), 0f, c);

            /// <summary>A shaft, tie rod or bar between two car-local points.
            /// Rotated rather than stepped, because a stepped diagonal at this
            /// scale reads as a broken part rather than as a driveshaft.</summary>
            public void Line(float x0, float y0, float x1, float y1, float width, Color c)
            {
                float dx = x1 - x0, dy = y1 - y0;
                float len = Mathf.Sqrt(dx * dx + dy * dy);
                if (len < 1e-4f) return;
                Make("line", new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f),
                     new Vector2(len, width), Mathf.Atan2(dy, dx) * Mathf.Rad2Deg, c);
            }

            public void Circle(float cx, float cy, float r, Color c)
            {
                var rt = Make("cyl", new Vector2(cx, cy), new Vector2(r * 2f, r * 2f), 0f, c);
                rt.GetComponent<Image>().sprite = Dot;
            }
        }

        static Sprite dot;
        /// <summary>A filled circle, generated once. Cylinder bores are circles
        /// in the source art and squares read as blocks rather than pots.</summary>
        static Sprite Dot
        {
            get
            {
                if (dot != null) return dot;
                const int n = 32;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
                { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x - (n - 1) * 0.5f) / (n * 0.5f);
                        float dy = (y - (n - 1) * 0.5f) / (n * 0.5f);
                        bool inside = dx * dx + dy * dy <= 1f;
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(inside ? 255 : 0));
                    }
                tex.SetPixels32(px); tex.Apply();
                dot = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
                return dot;
            }
        }

        // ------------------------------------------------------------------
        //  The drawing
        // ------------------------------------------------------------------
        /// <summary>
        /// Draw one car's running gear into a panel.
        /// </summary>
        /// <param name="panelW">Panel size in canvas units — the scale is solved
        /// from this rather than measured, because a rect created this frame has
        /// not resolved yet.</param>
        public static void Draw(RectTransform panel, float panelW, float panelH,
                                CarSpec spec, CarModelDef shell,
                                Inspection.Comp? highlight,
                                System.Func<Inspection.Comp, bool> isDone)
        {
            // Fall back to the reference FD's numbers when a car has no shell
            // baked — every one of these is a real measurement off that mesh.
            float L = shell != null ? shell.colliderSize.z : 4.1f;
            float W = shell != null ? shell.colliderSize.x : 1.72f;
            float wb = shell != null ? shell.wheelbase : 2.425f;
            float track = shell != null ? shell.trackWidth : 1.46f;
            float tyre = shell != null ? shell.wheelRadius : 0.31f;

            var pen = new Pen
            {
                panel = panel,
                // 0.94 leaves a hair of margin so the body outline is not
                // clipped by the panel edge.
                scale = Mathf.Min(panelW * 0.94f / L, panelH * 0.94f / W),
            };

            var idle = new Color(0.44f, 0.46f, 0.55f, 1f);
            var lit = new Color(1f, 0.80f, 0.25f, 1f);
            var done = new Color(0.38f, 0.72f, 0.45f, 1f);
            Color Col(Inspection.Comp c) =>
                highlight.HasValue && highlight.Value == c ? lit
                : (isDone != null && isDone(c)) ? done : idle;

            float F = wb * 0.5f, R = -wb * 0.5f;
            float halfTrack = track * 0.5f;
            float diffS = L * 0.055f;
            float barOff = L * 0.045f;
            float hair = L * 0.008f;      // the source's thinnest stroke

            var bodyC = Col(Inspection.Comp.Body);
            var wheelC = Col(Inspection.Comp.Wheels);
            var engineC = Col(Inspection.Comp.Engine);
            var transC = Col(Inspection.Comp.Transmission);
            var driveC = Col(Inspection.Comp.Driveline);
            var steerC = Col(Inspection.Comp.Steering);
            var coolC = Col(Inspection.Comp.Cooling);
            var suspC = Col(Inspection.Comp.Suspension);

            // Body outline, four edges — the running gear has to read as being
            // INSIDE the car rather than painted on top of a filled slab.
            pen.Box(0f, W * 0.5f - hair * 0.5f, L, hair, bodyC);
            pen.Box(0f, -W * 0.5f + hair * 0.5f, L, hair, bodyC);
            pen.Box(L * 0.5f - hair * 0.5f, 0f, hair, W, bodyC);
            pen.Box(-L * 0.5f + hair * 0.5f, 0f, hair, W, bodyC);

            // Wheels, at the shell's real axles and track.
            foreach (float ax in new[] { F, R })
                foreach (float side in new[] { -1f, 1f })
                    pen.Box(ax, side * halfTrack, tyre * 2f, tyre * 0.7f, wheelC);

            // Chassis furniture first, powertrain ink on top — the source's own
            // ordering, and the reason the sway bar can run through the block
            // without looking like part of it.
            DrawRadiator(pen, L, W, coolC);
            DrawSwayBar(pen, F, halfTrack, barOff, suspC);
            DrawSwayBar(pen, R, halfTrack, barOff, suspC);
            DrawTieRods(pen, F, halfTrack, L, steerC);

            string layout = (spec != null && !string.IsNullOrEmpty(spec.drv))
                ? spec.drv.ToUpperInvariant() : "FR";
            var shape = ShapeOf(spec != null ? spec.eType : null);
            var dims = EngineDims(shape, L, W);

            if (layout == "FF")
            {
                // Transverse on the front axle: block on one side of the bay,
                // gearbox continuing the same line across to the other.
                float span = halfTrack * 1.5f;
                float ex = F + L * 0.03f;
                float engCy = -span * 0.5f + dims.x * 0.5f;
                DrawEngine(pen, ex, engCy, shape, dims, engineC, transverse: true);
                float gb0 = engCy + dims.x * 0.5f + L * 0.01f;
                float gb1 = Mathf.Min(span * 0.5f, gb0 + L * 0.10f);
                pen.Box(ex, (gb0 + gb1) * 0.5f, W * 0.115f, Mathf.Abs(gb1 - gb0), transC);
                DrawDrivenAxle(pen, F, halfTrack, diffS, driveC);
            }
            else if (layout == "MR")
            {
                float ex = R + wb * 0.30f + dims.x * 0.1f;
                DrawEngine(pen, ex, 0f, shape, dims, engineC, transverse: false);
                DrawGearbox(pen, ex - dims.x * 0.5f, R - L * 0.02f, W, transC);
                DrawDrivenAxle(pen, R, halfTrack, diffS, driveC);
            }
            else if (layout == "RR")
            {
                // Engine hung behind the rear axle, gearbox reaching forward
                // past it — the 911 silhouette.
                float ex = R - dims.x * 0.32f - L * 0.035f;
                DrawGearbox(pen, ex + dims.x * 0.35f, R + L * 0.12f, W, transC);
                DrawDrivenAxle(pen, R, halfTrack, diffS, driveC);
                DrawEngine(pen, ex, 0f, shape, dims, engineC, transverse: false);
            }
            else
            {
                // FR and 4WD. The block's FRONT FACE sits just over the front
                // axle line — accessories ahead of it, crank back over the axle.
                // A centre-based formula slid long blocks progressively
                // rearward, which is what made trucks read "engine too far
                // back" in the source.
                float ex = F + L * 0.02f - dims.x * 0.5f;
                DrawEngine(pen, ex, 0f, shape, dims, engineC, transverse: false);
                float gb0 = ex - dims.x * 0.5f;
                float gb1 = gb0 - L * 0.115f;
                DrawGearbox(pen, gb0, gb1, W, transC);

                if (layout == "4WD")
                {
                    float tcS = L * 0.05f, tcH = W * 0.17f;
                    pen.Box(gb1 - tcS * 0.5f, 0f, tcS, tcH, transC);
                    // The front prop is offset to one side of the sump on a real
                    // 4WD but runs PARALLEL to the centreline; the offset is
                    // kept small enough that it terminates inside the front diff
                    // rather than beside it.
                    float py = Mathf.Min(W * 0.10f, diffS * 0.32f);
                    pen.Line(gb1 - tcS * 0.5f, py, F, py, L * 0.011f, driveC);
                    pen.Line(gb1 - tcS, 0f, R + diffS * 0.5f, 0f, L * 0.012f, driveC);
                    // Hidden-line the front halfshafts where they pass under the
                    // block, so the shafts stay readable without being painted
                    // across it.
                    DrawDrivenAxle(pen, F, halfTrack, diffS, driveC, dims.y * 0.5f);
                }
                else
                {
                    pen.Line(gb1, 0f, R + diffS * 0.5f, 0f, L * 0.012f, driveC);
                }
                DrawDrivenAxle(pen, R, halfTrack, diffS, driveC);
            }
        }

        /// <summary>Bore colour. The source strokes its cylinders as outlines,
        /// which reads against a same-coloured block; a FILLED circle in the
        /// block's own colour is invisible, which is what the first port drew.
        /// Punching them out in the panel's own ground is the equivalent that
        /// works with flat-filled UI images.</summary>
        static readonly Color Bore = new Color(0.05f, 0.05f, 0.09f, 1f);

        static void DrawEngine(Pen pen, float cx, float cy, EngineShape shape,
                               Vector2 dims, Color c, bool transverse)
        {
            // len runs along the crank; transverse swaps the axes.
            float len = dims.x, wid = dims.y;
            if (transverse) pen.Box(cx, cy, wid, len, c);
            else pen.Box(cx, cy, len, wid, c);

            // Cylinders. The pitch is solved from the block's real budget so
            // the bores and the bank stagger sit INSIDE it — laying them out on
            // len/n leaves the outer pots drawing over the block wall, which is
            // a bug the source hit and fixed.
            int n = Mathf.Max(1, shape.perBank);
            float draw = shape.kind == "rotary" ? 1.25f : 1f;
            float pitch = len / (n + 0.9f);
            float r = pitch * 0.34f * draw;
            float stagger = shape.banks > 1 ? pitch * 0.10f : 0f;
            float bankOff = shape.banks > 1
                ? (shape.kind == "flat" ? wid * 0.30f : wid * 0.22f) : 0f;

            for (int b = 0; b < Mathf.Max(1, shape.banks); b++)
            {
                float sign = b == 0 ? -1f : 1f;
                float across = shape.banks > 1 ? sign * bankOff : 0f;
                for (int i = 0; i < n; i++)
                {
                    float along = (i - (n - 1) * 0.5f) * pitch + (shape.banks > 1 ? sign * stagger : 0f);
                    if (transverse) pen.Circle(cx + across, cy + along, r, Bore);
                    else pen.Circle(cx + along, cy + across, r, Bore);
                }
            }
        }

        /// <summary>Gearbox: a tapered case in the source, a rect here — a
        /// four-point polygon is not something a UGUI Image can be.</summary>
        static void DrawGearbox(Pen pen, float x0, float x1, float W, Color c)
        {
            float len = Mathf.Abs(x1 - x0);
            if (len < 1e-4f) return;
            pen.Box((x0 + x1) * 0.5f, 0f, len, W * 0.115f, c);
        }

        static void DrawDrivenAxle(Pen pen, float axleX, float halfTrack,
                                   float diffS, Color c, float gapHalf = 0f)
        {
            float outer = halfTrack * 0.92f;
            float w = diffS * 0.22f;
            if (gapHalf > 0f && gapHalf < outer)
            {
                pen.Line(axleX, -outer, axleX, -gapHalf, w, c);
                pen.Line(axleX, gapHalf, axleX, outer, w, c);
            }
            else pen.Line(axleX, -outer, axleX, outer, w, c);

            // The diagrams' crossed box at the axle centre.
            pen.Box(axleX, 0f, diffS, diffS, c);
        }

        static void DrawTieRods(Pen pen, float F, float halfTrack, float L, Color c)
        {
            float x = F - L * 0.075f;
            float half = halfTrack * 0.55f;
            float w = L * 0.008f;
            // A short rack bar for the rods to pivot on. Without it both rods
            // hang off nothing, which the source's player noticed.
            pen.Line(x, -half, x, half, w, c);
            pen.Line(x, -half, F, -halfTrack * 0.85f, w * 0.85f, c);
            pen.Line(x, half, F, halfTrack * 0.85f, w * 0.85f, c);
        }

        static void DrawSwayBar(Pen pen, float axleX, float halfTrack, float off, Color c)
        {
            float x = axleX + off;
            float half = halfTrack * 0.78f;
            float w = pen.scale > 0f ? off * 0.18f : 0.02f;
            pen.Line(x, -half, x, half, w, c);
            pen.Line(x, -half, axleX, -half, w * 0.85f, c);
            pen.Line(x, half, axleX, half, w * 0.85f, c);
        }

        static void DrawRadiator(Pen pen, float L, float W, Color c)
        {
            float depth = L * 0.024f;
            float x = L * 0.47f - depth;
            pen.Box(x + depth * 0.5f, 0f, depth, W * 0.42f, c);
        }
    }
}
