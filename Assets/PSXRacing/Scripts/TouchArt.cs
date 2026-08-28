using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Every sprite the touch panel is made of, drawn in code.
    ///
    /// The panel is a port of Racing Game 2's mobile controls, which are SVG
    /// and CSS gradients — there is no artwork to import, only geometry to
    /// reproduce. Reproducing it in code rather than exporting PNGs keeps the
    /// numbers next to the source they came from: every constant below is the
    /// same number in `index.html` or `src/styles/base.css`, in the same units,
    /// and a change over there is a one-line change here.
    ///
    /// Drawn at 4x the CSS pixel size and left on the default bilinear filter.
    /// This canvas is at SCREEN resolution, not the 240-line framebuffer — the
    /// controls sit on top of the game rather than inside it, exactly as they do
    /// in the browser, so smooth is correct here and point-filtering would only
    /// make them look broken next to the CSS original.
    ///
    /// Everything is cached: these are ~200k pixel writes in total and they
    /// happen once, at Awake.
    /// </summary>
    public static class TouchArt
    {
        const int Up = 4;   // texture pixels per CSS pixel

        static Sprite circle, rounded, wheel, pedalBase, pedalArm;
        static Sprite gasFace, brakeFace, handbrake, shiftKnob, shiftRecess;

        // ------------------------------------------------------------------
        //  Drawing helpers
        // ------------------------------------------------------------------
        /// <summary>Signed distance to a rounded rectangle's edge, negative
        /// inside. Everything here is a rounded rectangle or a circle, and one
        /// SDF gives both the fill and the border without a second pass.</summary>
        static float RoundRect(float px, float py, float w, float h, float r)
        {
            float dx = Mathf.Max(0f, Mathf.Max(r - px, px - (w - r)));
            float dy = Mathf.Max(0f, Mathf.Max(r - py, py - (h - r)));
            return Mathf.Sqrt(dx * dx + dy * dy) - r;
        }

        /// <summary>Coverage from a signed distance: one pixel of antialiasing,
        /// which is what the browser gives these shapes.</summary>
        static float Cover(float sdf) => Mathf.Clamp01(0.5f - sdf);

        static Color32 Mix(Color32 a, Color32 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color32((byte)(a.r + (b.r - a.r) * t), (byte)(a.g + (b.g - a.g) * t),
                               (byte)(a.b + (b.b - a.b) * t), (byte)(a.a + (b.a - a.a) * t));
        }

        /// <summary>Three-stop vertical or horizontal ramp — the shape almost
        /// every CSS gradient in the source takes.</summary>
        static Color32 Ramp3(Color32 a, Color32 b, Color32 c, float mid, float t) =>
            t < mid ? Mix(a, b, t / Mathf.Max(mid, 1e-4f))
                    : Mix(b, c, (t - mid) / Mathf.Max(1f - mid, 1e-4f));

        static Color32 Rgb(int hex) =>
            new Color32((byte)((hex >> 16) & 0xFF), (byte)((hex >> 8) & 0xFF), (byte)(hex & 0xFF), 255);

        /// <summary>Build a sprite from a top-down painter. Texture row 0 is the
        /// BOTTOM in Unity and every dimension in the source CSS is measured
        /// from the top, so the flip happens here, once, instead of in each of
        /// the nine painters below.</summary>
        static Sprite Paint(int w, int h, System.Func<float, float, Color32> shade,
                            Vector4 border = default)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[(h - 1 - y) * w + x] = shade(x + 0.5f, y + 0.5f);
            tex.SetPixels32(px);
            tex.Apply();
            return border == default
                ? Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f))
                : Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f),
                                100f, 0, SpriteMeshType.FullRect, border);
        }

        // ------------------------------------------------------------------
        //  Generic shapes
        // ------------------------------------------------------------------
        public static Sprite Circle()
        {
            if (circle != null) return circle;
            const int S = 128;
            circle = Paint(S, S, (x, y) =>
            {
                float d = Mathf.Sqrt((x - S * 0.5f) * (x - S * 0.5f) + (y - S * 0.5f) * (y - S * 0.5f));
                return new Color32(255, 255, 255, (byte)(Cover(d - S * 0.5f + 1f) * 255f));
            });
            return circle;
        }

        public static Sprite Rounded()
        {
            if (rounded != null) return rounded;
            const int S = 64, R = 16;
            rounded = Paint(S, S, (x, y) =>
                new Color32(255, 255, 255, (byte)(Cover(RoundRect(x, y, S, S, R)) * 255f)),
                new Vector4(R, R, R, R));
            return rounded;
        }

        // ------------------------------------------------------------------
        //  Steering wheel
        // ------------------------------------------------------------------
        /// <summary>
        /// The wheel, to the source's numbers.
        ///
        /// `index.html` draws it in a 220-unit viewBox with the rim as a
        /// 22-wide stroke on r=89 — so the rim is the annulus from 78 to 100,
        /// and every radius below is that unit divided by 100.
        ///
        /// The three things the first version got wrong, all visible at a
        /// glance next to the browser:
        ///
        ///   SPOKES were angular wedges 4.5 to 11 degrees wide that got WIDER
        ///   toward the rim. The source spoke is a LINEAR flare — a slab 9 units
        ///   half-height at the hub opening to 25 at the rim — which is 22
        ///   degrees at the hub NARROWING to 16 at the rim. Wedges are thin
        ///   sticks where the original has cast-metal arms, and they taper the
        ///   wrong way.
        ///
        ///   STITCHING was 2.4 units wide at 40% duty and fully opaque. The
        ///   source is 1.2 wide, 29% duty (dasharray 2.5/6), at 55% alpha over
        ///   the leather. Ours read as a bright dotted ring rather than as
        ///   thread.
        ///
        ///   OUTLINES were missing entirely: the source strokes black at r=100
        ///   and r=78, which is what stops the rim bleeding into the scene
        ///   behind it.
        /// </summary>
        public static Sprite Wheel()
        {
            if (wheel != null) return wheel;
            // The only piece of this panel drawn SMALLER than it is displayed:
            // 300 canvas units on a phone whose scaler sits near 1.5 is 450
            // device pixels, and a 256 texture stretched to that is soft in
            // exactly the way the instruments were. Everything else here is
            // drawn at 4x its CSS size and downsamples.
            const int S = 512;
            const float RimOut = 1.00f, RimIn = 0.78f, Stitch = 0.83f;
            const float HubR = 0.27f, HubRing = 0.235f;
            // Spoke centre angles. The source rotates a +x-pointing spoke by
            // -8, 90 and 188 degrees in a y-DOWN SVG frame; this texture is
            // y-up, so each angle negates.
            float[] spokeDeg = { 8f, 172f, 270f };
            // The flare, in source units over a 100-unit radius: half-height 9
            // at r=22, 25 at r=87.
            const float SpokeR0 = 0.22f, SpokeR1 = 0.87f, SpokeH0 = 0.09f, SpokeH1 = 0.25f;

            var rimG = new[] { Rgb(0x050505), Rgb(0x1c1c1c), Rgb(0x3a3a3a), Rgb(0x1c1c1c), Rgb(0x000000) };
            var rimStop = new[] { 0.78f, 0.83f, 0.89f, 0.94f, 1.00f };

            wheel = Paint(S, S, (fx, fy) =>
            {
                float half = S * 0.5f;
                // Paint hands the shader DESIGN coordinates — y down, the CSS
                // convention every other painter here wants. This one is polar
                // maths and wants y UP, so it flips on the way in. Without it
                // the whole wheel is mirrored top to bottom: the twelve-o'clock
                // marker paints at six o'clock, and the three spokes sit at the
                // reflection of the stance they are meant to have.
                float dx = (fx - half) / half, dy = (half - fy) / half;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                if (ang < 0f) ang += 360f;
                float px = 1f / half;              // one texture pixel, in radius units

                Color32 col = new Color32(0, 0, 0, 0);
                float a = 0f;

                // Spokes first, so the rim and the hub cap paint over their
                // ends and the flared mounts blend in with no gap — the order
                // the source draws them in, and the reason it draws them first.
                if (d < RimOut)
                {
                    foreach (float sd in spokeDeg)
                    {
                        float off = Mathf.DeltaAngle(ang, sd) * Mathf.Deg2Rad;
                        float along = d * Mathf.Cos(off);
                        float across = Mathf.Abs(d * Mathf.Sin(off));
                        if (along < SpokeR0 * 0.5f || along > SpokeR1 + 0.06f) continue;
                        float halfH = Mathf.Lerp(SpokeH0, SpokeH1,
                            Mathf.InverseLerp(SpokeR0, SpokeR1, along));
                        float cov = Cover((across - halfH) / px);
                        if (cov <= 0f) continue;
                        // FLAT fill with a stroked edge, as the source has it.
                        // A centre-bright ramp across the width turns three cast
                        // arms into three beams of light fanning out of the hub.
                        col = Rgb(0x232323);
                        // The source's sheen: a thin lighter line just inside
                        // the leading edge, not a gradient across the whole arm.
                        float inset = halfH - across;
                        if (inset > 0f && inset < 1.5f * px && d * Mathf.Sin(off) < 0f)
                            col = Rgb(0x4A4A4A);
                        col = Mix(col, Rgb(0x000000), Cover((across - halfH + 0.6f * px) / (0.5f * px)));
                        a = Mathf.Max(a, cov);
                        break;
                    }
                }

                // Rim: a five-stop radial ramp across the annulus, which is what
                // makes it read as a round section catching light instead of a
                // flat ring.
                float rimCov = Cover((d - RimOut) / px) * Cover((RimIn - d) / px);
                if (rimCov > 0f)
                {
                    Color32 rc = rimG[0];
                    for (int i = 1; i < rimStop.Length; i++)
                        if (d <= rimStop[i] || i == rimStop.Length - 1)
                        {
                            rc = Mix(rimG[i - 1], rimG[i],
                                     Mathf.InverseLerp(rimStop[i - 1], rimStop[i], d));
                            break;
                        }

                    // Stitching: 1.2 units wide at r=83, dashes 2.5 on / 6 off
                    // along the circumference, 55% alpha. At r=83 that period is
                    // 8.5 units of arc, so 2 pi 83 / 8.5 = 61 dashes round.
                    float dash = Mathf.Repeat(ang, 360f / 61f) * (61f / 360f);
                    if (Mathf.Abs(d - Stitch) < 0.006f && dash < 2.5f / 8.5f)
                        rc = Mix(rc, Rgb(0x966E28), 0.55f);

                    // The twelve-o'clock marker, +/-5 degrees across the FULL
                    // rim width, with the source's vertical gradient.
                    if (Mathf.Abs(Mathf.DeltaAngle(ang, 90f)) <= 5f)
                    {
                        float t = Mathf.InverseLerp(RimOut, RimIn, d);
                        rc = Ramp3(Rgb(0xFFF080), Rgb(0xFFD400), Rgb(0xA87000), 0.35f, t);
                    }

                    // Black outlines at both edges of the annulus.
                    float lip = Mathf.Max(Cover((Mathf.Abs(d - RimOut) - 0.008f) / px),
                                          Cover((Mathf.Abs(d - RimIn) - 0.006f) / px));
                    rc = Mix(rc, Rgb(0x000000), lip);
                    col = rc; a = Mathf.Max(a, rimCov);
                }

                // Hub cap, painted last so it covers the spokes' inner ends.
                float hubCov = Cover((d - HubR) / px);
                if (hubCov > 0f)
                {
                    // Off-centre sheen, from the source's 40%/34% radial stop.
                    float sheen = Mathf.Clamp01(
                        1f - new Vector2(dx + 0.08f, dy - 0.10f).magnitude / (HubR * 1.55f));
                    Color32 hc = Ramp3(Rgb(0x484848), Rgb(0x202020), Rgb(0x090909), 0.55f, 1f - sheen);
                    hc = Mix(hc, Rgb(0x000000), Cover((Mathf.Abs(d - HubRing) - 0.004f) / px) * 0.45f);
                    // Abs, or this is not an outline: a raw signed distance
                    // is hugely negative everywhere INSIDE the cap, Cover reads
                    // that as full coverage, and the hub paints flat black over
                    // its own gradient. Which is exactly how it shipped.
                    hc = Mix(hc, Rgb(0x000000), Cover((Mathf.Abs(d - HubR) - 0.008f) / px));
                    col = hc; a = Mathf.Max(a, hubCov);
                }

                col.a = (byte)(Mathf.Clamp01(a) * 255f);
                return col;
            });
            return wheel;
        }

        // ------------------------------------------------------------------
        //  Pedal hardware
        // ------------------------------------------------------------------
        /// <summary>The mount the pedal arm pivots on: `.ped-base`, 36x14, a
        /// vertical steel ramp with a lit top edge.</summary>
        public static Sprite PedalBase()
        {
            if (pedalBase != null) return pedalBase;
            int w = 36 * Up, h = 14 * Up;
            pedalBase = Paint(w, h, (x, y) =>
            {
                float sdf = RoundRect(x, y, w, h, 2 * Up);
                float cov = Cover(sdf);
                if (cov <= 0f) return new Color32(0, 0, 0, 0);
                var c = Ramp3(Rgb(0x888888), Rgb(0x555555), Rgb(0x2C2C2C), 0.45f, y / h);
                c = Mix(c, Rgb(0xFFFFFF), Cover((y - 1.2f * Up) / Up) * 0.18f);   // inset top light
                c = Mix(c, Rgb(0x1A1A1A), Cover((sdf + Up) / (0.6f * Up)));       // 1px border
                c.a = (byte)(cov * 255f);
                return c;
            }, new Vector4(2 * Up, 2 * Up, 2 * Up, 2 * Up));
            return pedalBase;
        }

        /// <summary>`.ped-arm`: a 5x60 rod, lit down its centre line. Only the
        /// cross-section varies, so this is a thin strip stretched to length —
        /// which is also what makes scaling it in Y free.</summary>
        public static Sprite PedalArm()
        {
            if (pedalArm != null) return pedalArm;
            int w = 5 * Up, h = 4 * Up;
            pedalArm = Paint(w, h, (x, y) =>
                Ramp3(Rgb(0x3A3A3A), Rgb(0x9A9A9A), Rgb(0x3A3A3A), 0.5f, x / w));
            return pedalArm;
        }

        /// <summary>`.pedal-bar.gas .ped-face`: 26x62 perforated alloy, seven
        /// holes in the source's three-column zigzag.</summary>
        public static Sprite GasFace()
        {
            if (gasFace != null) return gasFace;
            int w = 26 * Up, h = 62 * Up;
            // Hole centres in CSS pixels, straight off the radial-gradient list.
            float[,] holes = { { 13, 8 }, { 7.8f, 18 }, { 18.2f, 18 }, { 13, 28 },
                               { 7.8f, 38 }, { 18.2f, 38 }, { 13, 48 } };
            gasFace = Paint(w, h, (x, y) =>
            {
                float sdf = RoundRect(x, y, w, h, 4 * Up);
                float cov = Cover(sdf);
                if (cov <= 0f) return new Color32(0, 0, 0, 0);
                var c = Mix(Rgb(0x363636), Rgb(0x1D1D1D), y / h);
                c = Mix(c, Rgb(0x00FF00), 0.06f);                                  // inset green cast
                for (int i = 0; i < holes.GetLength(0); i++)
                {
                    float hx = holes[i, 0] * Up, hy = holes[i, 1] * Up;
                    float hd = Mathf.Sqrt((x - hx) * (x - hx) + (y - hy) * (y - hy));
                    c = Mix(c, Rgb(0x050505), Cover(hd - 1.4f * Up));
                }
                c = Mix(c, Rgb(0x555555), Cover((sdf + Up) / (0.6f * Up)));
                c.a = (byte)(cov * 255f);
                return c;
            });
            return gasFace;
        }

        /// <summary>`.pedal-bar.brk .ped-face`: 30x38 of foam, speckled.</summary>
        public static Sprite BrakeFace()
        {
            if (brakeFace != null) return brakeFace;
            int w = 30 * Up, h = 38 * Up;
            float[,] spec = { { 22, 28 }, { 68, 18 }, { 40, 70 }, { 80, 60 }, { 15, 80 } };
            brakeFace = Paint(w, h, (x, y) =>
            {
                float sdf = RoundRect(x, y, w, h, 6 * Up);
                float cov = Cover(sdf);
                if (cov <= 0f) return new Color32(0, 0, 0, 0);
                var c = Mix(Rgb(0x2A2A2A), Rgb(0x171717), y / h);
                c = Mix(c, Rgb(0xFF3C3C), 0.07f);
                for (int i = 0; i < spec.GetLength(0); i++)
                {
                    float sx = spec[i, 0] * 0.01f * w, sy = spec[i, 1] * 0.01f * h;
                    float sd = Mathf.Sqrt((x - sx) * (x - sx) + (y - sy) * (y - sy));
                    c = Mix(c, Rgb(0xFFFFFF), Cover(sd - 0.8f * Up) * 0.07f);
                }
                c = Mix(c, Rgb(0x333333), Cover((sdf + Up) / (0.6f * Up)));
                c.a = (byte)(cov * 255f);
                return c;
            });
            return brakeFace;
        }

        /// <summary>
        /// The handbrake lever — `.ebh-rotor`, viewBox 22x110.
        ///
        /// On a phone the source hides the e-brake's pedal stack entirely and
        /// shows only this, so the lever IS the control: a ribbed grip with a
        /// chrome release button on the end. Ours had neither, which is why the
        /// handbrake read as an orange slab with a square on it.
        /// </summary>
        public static Sprite Handbrake()
        {
            if (handbrake != null) return handbrake;
            int w = 22 * Up, h = 110 * Up;
            handbrake = Paint(w, h, (x, y) =>
            {
                float u = x / Up, v = y / Up;         // back into source units
                Color32 c = new Color32(0, 0, 0, 0);
                float a = 0f;

                // Grip: x 7..15, from y=14 (rounded over) to the bottom.
                float gs = RoundRect(x - 7f * Up, y - 14f * Up, 8f * Up, 96f * Up, 4f * Up);
                // Only the TOP corners are round in the source path; square off
                // the bottom by ignoring the radius below the shoulder.
                if (v > 18f) gs = Mathf.Max(Mathf.Abs(u - 11f) - 4f, 0f) * Up - 0.5f;
                float gcov = Cover(gs);
                if (gcov > 0f)
                {
                    c = Ramp3(Rgb(0x0E0E0E), Rgb(0x2C2C2C), Rgb(0x050505), 0.48f,
                              Mathf.InverseLerp(7f, 15f, u));
                    // Seven moulded ribs, and the specular stripe down the left
                    // third that makes the grip read as round.
                    for (int i = 0; i < 7; i++)
                    {
                        float ry = 24f + i * 12f;
                        c = Mix(c, Rgb(0x000000), Cover((Mathf.Abs(v - ry) - 0.15f) * Up) * 0.55f);
                    }
                    if (u > 7.5f && u < 8.8f) c = Mix(c, Rgb(0xFFFFFF), 0.10f);
                    c = Mix(c, Rgb(0x000000), Cover((gs + 0.4f * Up) / (0.4f * Up)) * 0.8f);
                    a = gcov;
                }

                // Shadow under the button collar, then the collar, then the
                // chromed cap and its highlight.
                float col = Cover((Mathf.Sqrt(Mathf.Pow((u - 11f) / 4f, 2f) +
                                              Mathf.Pow((v - 14.2f) / 0.8f, 2f)) - 1f) * Up);
                if (col > 0f) { c = Mix(c, Rgb(0x000000), 0.65f * col); a = Mathf.Max(a, col); }

                float btn = Cover(RoundRect(x - 9.2f * Up, y - 10f * Up, 3.6f * Up, 4f * Up, 0.4f * Up));
                if (btn > 0f)
                {
                    c = Ramp3(Rgb(0x5E5E5E), Rgb(0xD4D4D4), Rgb(0x3A3A3A), 0.35f,
                              Mathf.InverseLerp(9.2f, 12.8f, u));
                    a = Mathf.Max(a, btn);
                }
                float cap = Cover((Mathf.Sqrt(Mathf.Pow((u - 11f) / 1.8f, 2f) +
                                              Mathf.Pow((v - 10f) / 0.7f, 2f)) - 1f) * Up);
                if (cap > 0f)
                {
                    float sheen = Mathf.Clamp01(1f - Mathf.Abs(u - 10.2f) / 2.2f);
                    c = Ramp3(Rgb(0xF0F0F0), Rgb(0xBABABA), Rgb(0x5A5A5A), 0.4f, 1f - sheen);
                    a = Mathf.Max(a, cap);
                }

                c.a = (byte)(Mathf.Clamp01(a) * 255f);
                return c;
            });
            return handbrake;
        }

        /// <summary>The shifter puck: 44px, lit from upper left.</summary>
        public static Sprite ShiftKnob()
        {
            if (shiftKnob != null) return shiftKnob;
            int s = 44 * Up;
            shiftKnob = Paint(s, s, (x, y) =>
            {
                float d = Mathf.Sqrt((x - s * 0.5f) * (x - s * 0.5f) + (y - s * 0.5f) * (y - s * 0.5f));
                float cov = Cover(d - s * 0.5f + 1f);
                if (cov <= 0f) return new Color32(0, 0, 0, 0);
                float lit = Mathf.Clamp01(
                    Mathf.Sqrt(Mathf.Pow(x - s * 0.32f, 2f) + Mathf.Pow(y - s * 0.28f, 2f)) / (s * 0.8f));
                var c = Ramp3(Rgb(0x4A4A4A), Rgb(0x262626), Rgb(0x0A0A0A), 0.45f, lit);
                // The source's inset bottom shadow, which seats the knob.
                c = Mix(c, Rgb(0x000000), Mathf.Clamp01((y / s - 0.62f) * 2.2f) * 0.45f);
                c.a = (byte)(cov * 255f);
                return c;
            });
            return shiftKnob;
        }

        /// <summary>The recess the gear number sits in: 30px, bevelled bright at
        /// the top and dark at the bottom, per the source's four-sided
        /// border-color.</summary>
        public static Sprite ShiftRecess()
        {
            if (shiftRecess != null) return shiftRecess;
            int s = 30 * Up;
            shiftRecess = Paint(s, s, (x, y) =>
            {
                float dx = x - s * 0.5f, dy = y - s * 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float cov = Cover(d - s * 0.5f + 1f);
                if (cov <= 0f) return new Color32(0, 0, 0, 0);
                var c = Mix(Rgb(0x1C1C1C), Rgb(0x050505), Mathf.Clamp01(d / (s * 0.5f)));
                // Bevel ring: #b8b8b8 top, #9a9a9a sides, #5e5e5e bottom.
                float ring = Cover((Mathf.Abs(d - (s * 0.5f - 1.5f * Up)) - 0.75f * Up) / Up);
                if (ring > 0f)
                {
                    float up = -dy / Mathf.Max(d, 1e-4f);         // +1 at the top
                    Color32 bev = up > 0f ? Mix(Rgb(0x9A9A9A), Rgb(0xB8B8B8), up)
                                          : Mix(Rgb(0x9A9A9A), Rgb(0x5E5E5E), -up);
                    c = Mix(c, bev, ring);
                }
                c.a = (byte)(cov * 255f);
                return c;
            });
            return shiftRecess;
        }
    }
}
