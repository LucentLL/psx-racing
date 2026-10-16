using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE v08 COCKPIT'S FOUR GAUGES, drawn in game (owner, 2026-10-16: "Temp
    /// and Fuel gauges get their own gauges now"; the faces "should remain
    /// blank to be drawn in game").
    ///
    /// The style is the owner's reference, a 90s four-dial cluster: a big
    /// TACH (white numerals on black, an orange-red redline arc, x1000 r/min,
    /// an LCD slot under the hub that shows the gear) and a big SPEEDO (the
    /// player's unit in white outside, the other one small and ORANGE inside),
    /// with a small TEMP (C..H, red over the engine model's red mark, a
    /// coolant icon) and a small FUEL (E..F, a pump icon) outboard. Thin white
    /// ticks, red needles, black caps. No brand, no logo.
    ///
    /// Each face is painted ONCE per car into a small texture (256 px for the
    /// big dials, 128 for the small), scaled to that car: the tach to its rev
    /// limiter (the 2D cluster's rule, ceil(limit x 1.1) to the thousand) with
    /// the red from its redline; the speedo to what the car as BUILT can
    /// reach. Repainted only when that scale or the speed unit changes. The
    /// needles and the gear are drawn by PSX/CabinGauge from four uniforms
    /// per dial, so nothing is written to a texture per frame.
    /// </summary>
    public class CockpitGauges
    {
        public const int BigPx = 256, SmallPx = 128;
        /// <summary>Sweeps, degrees clockwise from up: the big dials' zero
        /// and full scale, the small dials' empty and full.</summary>
        public const float BigFrom = -125f, BigTo = 125f, SmallFrom = -50f, SmallTo = 50f;
        /// <summary>The backlight by day (readable in the shade, dim) and at night.</summary>
        public const float DayGlow = 0.55f, NightGlow = 1.0f;
        /// <summary>The LCD gear window on the tach face: centre and half size, UV.</summary>
        public static readonly Vector4 Lcd = new Vector4(0.5f, 0.27f, 0.075f, 0.058f);

        static readonly Color32 Ink = new Color32(236, 236, 230, 255);
        static readonly Color32 Red = new Color32(255, 52, 24, 255);
        static readonly Color32 Orange = new Color32(255, 132, 28, 255);
        static readonly Color32 Black = new Color32(9, 9, 11, 255);
        static readonly Color32 LcdPane = new Color32(118, 136, 112, 255);

        public class Dial
        {
            public string key;
            public Material mat;
            public Texture2D face;
            public float deg = float.NaN;
        }

        readonly Dictionary<string, Dial> dials = new Dictionary<string, Dial>();
        float tachMax, redFrom, speedMax;
        bool mph;
        int unitsRev = -1;
        float flutter;
        float lastGlow = -1f;
        int lastGear = int.MinValue;

        public float TachMax => tachMax;
        public float SpeedMax => speedMax;
        public bool OuterMph => mph;
        public IEnumerable<Dial> All => dials.Values;
        public Dial Get(string key) => dials.TryGetValue(key, out var d) ? d : null;

        /// <summary>One dial on a renderer: its own material (the handoff:
        /// clone the blank PER FACE) and, in an RHD cabin, its canvas's U
        /// flipped back to the driver's left-to-right.</summary>
        public void Add(string key, Renderer r, bool flipU)
        {
            var sh = Shader.Find("PSX/CabinGauge");
            if (sh == null || r == null) return;
            var d = new Dial { key = key };
            d.mat = new Material(sh) { name = "Gauge_" + key, hideFlags = HideFlags.HideAndDontSave };
            d.mat.SetFloat("_FlipU", flipU ? 1f : 0f);
            bool big = key == "tach" || key == "speed";
            d.mat.SetVector("_Needle", big ? new Vector4(0f, 0.84f, 0.17f, 0.024f) : new Vector4(0f, 0.80f, 0.20f, 0.045f));
            d.mat.SetVector("_Lcd", Lcd);
            d.mat.SetFloat("_Gear", 99f);
            r.sharedMaterial = d.mat;
            dials[key] = d;
        }

        public void Destroy()
        {
            foreach (var d in dials.Values)
            {
                if (d.face != null) Object.Destroy(d.face);
                if (d.mat != null) Object.Destroy(d.mat);
            }
            dials.Clear();
        }

        // ------------------------------------------------------------------
        //  The scales and the faces
        // ------------------------------------------------------------------

        /// <summary>The tach's full scale for a rev limiter (GaugeCluster's rule).</summary>
        public static float TachMaxFor(float revLimit) => Mathf.Max(6000f, Mathf.Ceil(revLimit * 1.1f / 1000f) * 1000f);

        /// <summary>The speedo's full scale, in its outer unit, for a top speed in km/h:
        /// the next 20 above top x 1.08 (10 under 120).</summary>
        public static float SpeedMaxFor(float topKmh, bool mph)
        {
            float top = (mph ? topKmh * 0.621371f : topKmh) * 1.08f;
            float step = top <= 120f ? 10f : 20f;
            return Mathf.Clamp(Mathf.Ceil(top / step) * step, mph ? 80f : 140f, mph ? 300f : 480f);
        }

        /// <summary>Paint every face for this car, if its scale changed.</summary>
        public void Refresh(CarController car)
        {
            float limit = car != null ? car.revLimitRPM : 8000f;
            float red = car != null ? car.redlineRPM : 7000f;
            float topKmh = car != null ? Mathf.Max(car.BuildTopSpeedMps, car.ReachableTopSpeedMps) * 3.6f : 240f;
            bool m = SpeedUnits.Mph;
            float t = TachMaxFor(limit), s = SpeedMaxFor(topKmh, m), rf = Mathf.Clamp01(red / t);
            if (t == tachMax && s == speedMax && rf == redFrom && m == mph && unitsRev == SpeedUnits.Changed && FacesExist()) return;
            tachMax = t; speedMax = s; redFrom = rf; mph = m; unitsRev = SpeedUnits.Changed;
            foreach (var d in dials.Values)
            {
                bool big = d.key == "tach" || d.key == "speed";
                int n = big ? BigPx : SmallPx;
                var px = new Color32[n * n];
                switch (d.key)
                {
                    case "tach": PaintTach(px, n); break;
                    case "speed": PaintSpeed(px, n); break;
                    case "temp": PaintSmall(px, n, "C", "H", EngineTemp.RedFrac, true); break;
                    default: PaintSmall(px, n, "E", "F", -1f, false); break;
                }
                if (d.face == null)
                    d.face = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "GaugeFace_" + d.key, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 2, hideFlags = HideFlags.HideAndDontSave };
                d.face.SetPixels32(px);
                d.face.Apply(true, false);
                d.mat.SetTexture("_FaceTex", d.face);
            }
        }

        bool FacesExist()
        {
            foreach (var d in dials.Values) if (d.face == null) return false;
            return true;
        }

        void PaintTach(Color32[] px, int n)
        {
            var c = new Paint(px, n);
            c.Fill(Black);
            int k = Mathf.RoundToInt(tachMax / 1000f);
            c.ArcBand(0.44f, 0.475f, Lerp(BigFrom, BigTo, redFrom), BigTo, Red);
            int sub = k <= 9 ? 4 : 2;
            for (int i = 0; i <= k * sub; i++)
            {
                float v = i / (float)(k * sub), a = Lerp(BigFrom, BigTo, v);
                bool major = i % sub == 0, half = !major && (i * 2) % sub == 0;
                c.Tick(a, major ? 0.385f : half ? 0.415f : 0.435f, 0.465f, major ? 0.013f : half ? 0.008f : 0.005f, Ink);
                if (major)
                    c.Text((i / sub).ToString(), a, 0.31f, 0.085f, 0.016f, v >= redFrom - 1e-3f ? Red : Ink);
            }
            c.TextAt("x1000r/min", 0.5f, 0.66f, 0.032f, 0.007f, Ink);
            c.Rect(Lcd.x, Lcd.y, Lcd.z + 0.012f, Lcd.w + 0.012f, new Color32(40, 42, 44, 255));
            c.Rect(Lcd.x, Lcd.y, Lcd.z, Lcd.w, LcdPane);
        }

        void PaintSpeed(Color32[] px, int n)
        {
            var c = new Paint(px, n);
            c.Fill(Black);
            float step = speedMax <= 120f ? 10f : 20f;
            int steps = Mathf.RoundToInt(speedMax / step);
            for (int i = 0; i <= steps * 2; i++)
            {
                float v = i / (float)(steps * 2), a = Lerp(BigFrom, BigTo, v);
                bool major = i % 2 == 0;
                c.Tick(a, major ? 0.39f : 0.425f, 0.465f, major ? 0.012f : 0.007f, Ink);
                if (major)
                {
                    int val = Mathf.RoundToInt(i / 2 * step);
                    bool show = steps <= 12 || (i / 2) % 2 == 0;
                    if (show) c.Text(val.ToString(), a, 0.32f, val >= 100 ? 0.066f : 0.074f, 0.013f, Ink);
                }
            }
            // The other unit, small and orange, on an inner ring.
            float inMax = mph ? speedMax / 0.621371f : speedMax * 0.621371f;
            float inStep = inMax <= 130f ? 10f : inMax <= 260f ? 20f : 40f;
            for (float val = inStep; val <= inMax + 0.01f; val += inStep)
            {
                float a = Lerp(BigFrom, BigTo, val / inMax);
                c.Tick(a, 0.228f, 0.252f, 0.006f, Orange);
                // Small and sparse: the reference's inner scale is a hint, not a second dial.
                if (Mathf.RoundToInt(val / inStep) % 2 == 0 || inMax / inStep <= 6)
                    c.Text(Mathf.RoundToInt(val).ToString(), a, 0.183f, 0.034f, 0.007f, Orange);
            }
            c.TextAt(mph ? "MPH" : "km/h", 0.5f, 0.33f, 0.05f, 0.011f, Ink);
            c.TextAt(mph ? "km/h" : "MPH", 0.5f, 0.265f, 0.036f, 0.008f, Orange);
        }

        void PaintSmall(Color32[] px, int n, string lo, string hi, float red, bool coolant)
        {
            var c = new Paint(px, n);
            c.Fill(Black);
            if (red >= 0f) c.ArcBand(0.395f, 0.455f, Lerp(SmallFrom, SmallTo, red), SmallTo, Red);
            for (int i = 0; i <= 4; i++)
            {
                float a = Lerp(SmallFrom, SmallTo, i / 4f);
                bool end = i == 0 || i == 4, mid = i == 2;
                c.Tick(a, end || mid ? 0.335f : 0.375f, 0.455f, end || mid ? 0.03f : 0.02f, end && i == 4 && red >= 0f ? Red : Ink);
            }
            c.Text(lo, SmallFrom - 16f, 0.36f, 0.13f, 0.03f, Ink);
            c.Text(hi, SmallTo + 16f, 0.36f, 0.13f, 0.03f, red >= 0f ? Red : Ink);
            if (coolant) c.Thermometer(0.5f, 0.27f, 0.13f, Ink);
            else c.Pump(0.5f, 0.27f, 0.13f, Ink);
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        // ------------------------------------------------------------------
        //  Per frame: the needles, the gear, the backlight
        // ------------------------------------------------------------------

        public void Tick(float dt, CarController car, GaugeCluster cluster, FuelTank tank, EngineTemp temp, bool backlit)
        {
            float glow = backlit ? NightGlow : DayGlow;
            bool glowChanged = glow != lastGlow;
            lastGlow = glow;
            bool dead = cluster != null && cluster.hideGauges;
            float rpm = car != null ? car.currentRPM : 0f;
            if (cluster != null && cluster.rpmFlutter)
            {
                flutter += dt;
                rpm *= 0.55f + Mathf.PerlinNoise(flutter * 2.3f, 0f) * 0.9f;
            }
            foreach (var d in dials.Values)
            {
                if (glowChanged) d.mat.SetFloat("_Glow", glow);
                float v, tau, from, to;
                switch (d.key)
                {
                    case "tach": v = rpm / Mathf.Max(1f, tachMax); tau = 0.05f; from = BigFrom; to = BigTo; break;
                    case "speed":
                        float sp = car != null ? Mathf.Abs(car.speedKmh) : 0f;
                        v = (mph ? sp * 0.621371f : sp) / Mathf.Max(1f, speedMax); tau = 0.08f; from = BigFrom; to = BigTo; break;
                    case "fuel": v = tank != null ? tank.percent * 0.01f : 1f; tau = 0.8f; from = SmallFrom; to = SmallTo; break;
                    default: v = temp != null ? temp.Gauge : 0.5f; tau = 0.8f; from = SmallFrom; to = SmallTo; break;
                }
                if (dead) v = 0f;
                float want = Lerp(from, to, Mathf.Clamp01(v));
                float deg = float.IsNaN(d.deg) || dt <= 0f ? want : Mathf.Lerp(d.deg, want, 1f - Mathf.Exp(-dt / tau));
                if (!float.IsNaN(d.deg) && Mathf.Abs(deg - d.deg) < 0.03f) continue;
                d.deg = deg;
                var nv = d.mat.GetVector("_Needle");
                nv.x = deg * Mathf.Deg2Rad;
                d.mat.SetVector("_Needle", nv);
            }
            var tach = Get("tach");
            if (tach != null)
            {
                int gear = dead || car == null ? 99 : car.currentGear;
                if (gear != lastGear) { lastGear = gear; tach.mat.SetFloat("_Gear", gear); }
            }
        }

        // ------------------------------------------------------------------
        //  The painter: anti-aliased strokes on a square canvas, UV units
        // ------------------------------------------------------------------

        struct Paint
        {
            readonly Color32[] px;
            readonly int n;
            public Paint(Color32[] px, int n) { this.px = px; this.n = n; }

            void Blend(int i, int j, Color32 c, float a)
            {
                if (a <= 0f || i < 0 || j < 0 || i >= n || j >= n) return;
                int k = j * n + i;
                var d = px[k];
                a = Mathf.Clamp01(a);
                px[k] = new Color32((byte)(d.r + (c.r - d.r) * a), (byte)(d.g + (c.g - d.g) * a), (byte)(d.b + (c.b - d.b) * a), 255);
            }

            /// <summary>The face's ground (the geometry clips it to a disc).</summary>
            public void Fill(Color32 c)
            {
                for (int k = 0; k < px.Length; k++) px[k] = c;
            }

            /// <summary>A round-capped line from a to b, `w` wide (UV).</summary>
            public void Line(Vector2 a, Vector2 b, float w, Color32 c)
            {
                float hw = w * 0.5f, pxw = 1f / n;
                int i0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - hw) * n) - 1, i1 = Mathf.CeilToInt((Mathf.Max(a.x, b.x) + hw) * n) + 1;
                int j0 = Mathf.FloorToInt((Mathf.Min(a.y, b.y) - hw) * n) - 1, j1 = Mathf.CeilToInt((Mathf.Max(a.y, b.y) + hw) * n) + 1;
                Vector2 ab = b - a;
                float l2 = Mathf.Max(ab.sqrMagnitude, 1e-12f);
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        var p = new Vector2((i + 0.5f) / n, (j + 0.5f) / n);
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
                        float d = (p - (a + ab * t)).magnitude;
                        Blend(i, j, c, (hw - d) / pxw + 0.5f);
                    }
            }

            static Vector2 Polar(float deg, float r) =>
                new Vector2(0.5f + Mathf.Sin(deg * Mathf.Deg2Rad) * r, 0.5f + Mathf.Cos(deg * Mathf.Deg2Rad) * r);

            public void Tick(float deg, float r0, float r1, float w, Color32 c) => Line(Polar(deg, r0), Polar(deg, r1), w, c);

            public void ArcBand(float r0, float r1, float d0, float d1, Color32 c)
            {
                float pxw = 1f / n;
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        float x = (i + 0.5f) / n - 0.5f, y = (j + 0.5f) / n - 0.5f;
                        float r = Mathf.Sqrt(x * x + y * y);
                        if (r < r0 - pxw || r > r1 + pxw) continue;
                        float a = Mathf.Atan2(x, y) * Mathf.Rad2Deg;
                        float cov = Mathf.Min(Mathf.Clamp01((r - r0) / pxw + 0.5f), Mathf.Clamp01((r1 - r) / pxw + 0.5f));
                        float ang = Mathf.Min(Mathf.Clamp01((a - d0) / (pxw / Mathf.Max(r, 1e-3f) * Mathf.Rad2Deg) + 0.5f),
                                              Mathf.Clamp01((d1 - a) / (pxw / Mathf.Max(r, 1e-3f) * Mathf.Rad2Deg) + 0.5f));
                        Blend(i, j, c, cov * ang);
                    }
            }

            public void Rect(float cx, float cy, float hx, float hy, Color32 c)
            {
                float pxw = 1f / n;
                for (int j = Mathf.FloorToInt((cy - hy) * n) - 1; j <= Mathf.CeilToInt((cy + hy) * n); j++)
                    for (int i = Mathf.FloorToInt((cx - hx) * n) - 1; i <= Mathf.CeilToInt((cx + hx) * n); i++)
                    {
                        float x = Mathf.Abs((i + 0.5f) / n - cx), y = Mathf.Abs((j + 0.5f) / n - cy);
                        Blend(i, j, c, Mathf.Min(Mathf.Clamp01((hx - x) / pxw + 0.5f), Mathf.Clamp01((hy - y) / pxw + 0.5f)));
                    }
            }

            /// <summary>Text centred on a point on the dial at angle/radius.</summary>
            public void Text(string s, float deg, float r, float h, float w, Color32 c)
            {
                var p = Polar(deg, r);
                TextAt(s, p.x, p.y, h, w, c);
            }

            /// <summary>Stroke-font text centred at (cx, cy), cap height h.</summary>
            public void TextAt(string s, float cx, float cy, float h, float w, Color32 c)
            {
                float adv = h * 0.72f, gw = h * 0.56f;
                float total = s.Length * adv - (adv - gw);
                float x0 = cx - total * 0.5f, y0 = cy - h * 0.5f;
                for (int k = 0; k < s.Length; k++)
                {
                    if (!Glyphs.TryGetValue(s[k], out var strokes)) continue;
                    foreach (var st in strokes)
                        for (int m = 0; m + 3 < st.Length; m += 2)
                            Line(new Vector2(x0 + st[m] * gw, y0 + st[m + 1] * h), new Vector2(x0 + st[m + 2] * gw, y0 + st[m + 3] * h), w, c);
                    x0 += adv;
                }
            }

            public void Thermometer(float cx, float cy, float s, Color32 c)
            {
                float w = s * 0.11f;
                Line(new Vector2(cx, cy - s * 0.12f), new Vector2(cx, cy + s * 0.45f), w, c);
                Disc2(cx, cy - s * 0.2f, s * 0.12f, c);
                for (int k = 0; k < 3; k++) Line(new Vector2(cx + s * 0.05f, cy + s * (0.1f + k * 0.12f)), new Vector2(cx + s * 0.2f, cy + s * (0.1f + k * 0.12f)), w * 0.6f, c);
                for (int k = 0; k < 4; k++)   // the waves it stands in
                {
                    float x = cx - s * 0.45f + k * s * 0.225f;
                    Line(new Vector2(x, cy - s * 0.42f), new Vector2(x + s * 0.11f, cy - s * 0.34f), w * 0.6f, c);
                    Line(new Vector2(x + s * 0.11f, cy - s * 0.34f), new Vector2(x + s * 0.225f, cy - s * 0.42f), w * 0.6f, c);
                }
            }

            public void Pump(float cx, float cy, float s, Color32 c)
            {
                float w = s * 0.09f;
                float l = cx - s * 0.32f, r = cx + s * 0.12f, b = cy - s * 0.42f, t = cy + s * 0.42f;
                Line(new Vector2(l, b), new Vector2(l, t), w, c); Line(new Vector2(l, t), new Vector2(r, t), w, c);
                Line(new Vector2(r, t), new Vector2(r, b), w, c); Line(new Vector2(l - s * 0.08f, b), new Vector2(r + s * 0.08f, b), w, c);
                Rect((l + r) * 0.5f, cy + s * 0.2f, s * 0.13f, s * 0.1f, c);
                Line(new Vector2(r, cy + s * 0.1f), new Vector2(cx + s * 0.32f, cy - s * 0.05f), w * 0.8f, c);
                Line(new Vector2(cx + s * 0.32f, cy - s * 0.05f), new Vector2(cx + s * 0.32f, cy + s * 0.3f), w * 0.8f, c);
                Line(new Vector2(cx + s * 0.32f, cy + s * 0.3f), new Vector2(cx + s * 0.22f, cy + s * 0.4f), w * 0.8f, c);
            }

            void Disc2(float cx, float cy, float r, Color32 c)
            {
                float pxw = 1f / n;
                for (int j = Mathf.FloorToInt((cy - r) * n) - 1; j <= Mathf.CeilToInt((cy + r) * n); j++)
                    for (int i = Mathf.FloorToInt((cx - r) * n) - 1; i <= Mathf.CeilToInt((cx + r) * n); i++)
                    {
                        float d = new Vector2((i + 0.5f) / n - cx, (j + 0.5f) / n - cy).magnitude;
                        Blend(i, j, c, (r - d) / pxw + 0.5f);
                    }
            }
        }

        /// <summary>A small stroke font in a unit cell (x 0..1, y 0..1 up):
        /// each glyph is polylines of x,y pairs.</summary>
        static readonly Dictionary<char, float[][]> Glyphs = new Dictionary<char, float[][]>
        {
            ['0'] = new[] { new[] { 0f, .15f, 0f, .85f, .15f, 1f, .85f, 1f, 1f, .85f, 1f, .15f, .85f, 0f, .15f, 0f, 0f, .15f } },
            ['1'] = new[] { new[] { .2f, .8f, .55f, 1f, .55f, 0f } },
            ['2'] = new[] { new[] { 0f, .8f, .15f, 1f, .85f, 1f, 1f, .85f, 1f, .6f, 0f, 0f, 1f, 0f } },
            ['3'] = new[] { new[] { 0f, 1f, 1f, 1f, .45f, .58f, .85f, .58f, 1f, .42f, 1f, .15f, .85f, 0f, .15f, 0f, 0f, .15f } },
            ['4'] = new[] { new[] { .75f, 0f, .75f, 1f, 0f, .3f, 1f, .3f } },
            ['5'] = new[] { new[] { 1f, 1f, .05f, 1f, 0f, .55f, .8f, .6f, 1f, .42f, 1f, .15f, .85f, 0f, .15f, 0f, 0f, .15f } },
            ['6'] = new[] { new[] { .9f, 1f, .3f, 1f, 0f, .6f, 0f, .15f, .15f, 0f, .85f, 0f, 1f, .15f, 1f, .45f, .85f, .6f, .15f, .6f, 0f, .45f } },
            ['7'] = new[] { new[] { 0f, 1f, 1f, 1f, .35f, 0f } },
            ['8'] = new[] { new[] { .15f, .55f, .05f, .65f, .05f, .9f, .15f, 1f, .85f, 1f, .95f, .9f, .95f, .65f, .85f, .55f, .15f, .55f, 0f, .4f, 0f, .15f, .15f, 0f, .85f, 0f, 1f, .15f, 1f, .4f, .85f, .55f } },
            ['9'] = new[] { new[] { .1f, 0f, .7f, 0f, 1f, .4f, 1f, .85f, .85f, 1f, .15f, 1f, 0f, .85f, 0f, .55f, .15f, .4f, .85f, .4f, 1f, .55f } },
            ['C'] = new[] { new[] { 1f, .85f, .85f, 1f, .15f, 1f, 0f, .85f, 0f, .15f, .15f, 0f, .85f, 0f, 1f, .15f } },
            ['H'] = new[] { new[] { 0f, 0f, 0f, 1f }, new[] { 1f, 0f, 1f, 1f }, new[] { 0f, .5f, 1f, .5f } },
            ['E'] = new[] { new[] { 1f, 1f, 0f, 1f, 0f, 0f, 1f, 0f }, new[] { 0f, .5f, .75f, .5f } },
            ['F'] = new[] { new[] { 1f, 1f, 0f, 1f, 0f, 0f }, new[] { 0f, .5f, .75f, .5f } },
            ['M'] = new[] { new[] { 0f, 0f, 0f, 1f, .5f, .4f, 1f, 1f, 1f, 0f } },
            ['P'] = new[] { new[] { 0f, 0f, 0f, 1f, .85f, 1f, 1f, .85f, 1f, .6f, .85f, .45f, 0f, .45f } },
            ['k'] = new[] { new[] { 0f, 0f, 0f, 1f }, new[] { .9f, .65f, 0f, .25f }, new[] { .3f, .38f, .9f, 0f } },
            ['m'] = new[] { new[] { 0f, 0f, 0f, .65f }, new[] { 0f, .5f, .15f, .65f, .4f, .65f, .5f, .5f, .5f, 0f }, new[] { .5f, .5f, .6f, .65f, .85f, .65f, 1f, .5f, 1f, 0f } },
            ['h'] = new[] { new[] { 0f, 0f, 0f, 1f }, new[] { 0f, .45f, .2f, .65f, .8f, .65f, 1f, .45f, 1f, 0f } },
            ['n'] = new[] { new[] { 0f, 0f, 0f, .65f }, new[] { 0f, .45f, .2f, .65f, .8f, .65f, 1f, .45f, 1f, 0f } },
            ['r'] = new[] { new[] { 0f, 0f, 0f, .65f }, new[] { 0f, .4f, .3f, .65f, .9f, .65f } },
            ['i'] = new[] { new[] { .5f, 0f, .5f, .62f }, new[] { .5f, .86f, .5f, .9f } },
            ['x'] = new[] { new[] { 0f, 0f, 1f, .65f }, new[] { 0f, .65f, 1f, 0f } },
            ['/'] = new[] { new[] { 0f, 0f, 1f, 1f } },
        };
    }
}
