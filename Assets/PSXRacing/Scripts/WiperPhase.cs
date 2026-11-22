using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE WIPERS' PHASE MAPS (rainglass, 2026-11-22). The windscreen's glass
    /// shader clears the water a blade has passed. From v10 the cockpit's
    /// modeller delivers WHERE the rubber passes as two maps in the pane's UV0
    /// (Cockpit_Wipers_Codex_Brief_2026-11-22.md, section 4): 256 x 128 grey,
    /// 0 = the rubber never reaches this texel, 1..255 = the stroke value s
    /// (0 parked .. 1 top of the stroke) at which it crosses it on the upstroke.
    /// Until then the same maps are made here from the drawn blades' arcs, so
    /// the shader has one path for both.
    ///
    /// The shader gets ONE linear RGBA texture, bilinear:
    ///   r  the driver's arm's phase s, g  the passenger's (each spread a few
    ///      texels past its swept area, so filtering at the edge reads the
    ///      edge's own phase, not a blend with "never");
    ///   b, a  each arm's SIGNED DISTANCE to the edge of its swept area,
    ///      0.5 + d / (2 FieldM) (inside positive, pane metres): an exact
    ///      anti-aliased edge at any size, and the bank of water the blade
    ///      leaves just outside it.
    /// Made once per cockpit glass material (two 256 x 128 passes, about a
    /// millisecond), never per frame.
    /// </summary>
    public static class WiperPhase
    {
        public const int W = 256, H = 128;
        /// <summary>The distance field's reach either side of an edge, pane metres.</summary>
        public const float FieldM = 0.04f;
        /// <summary>The modeller's maps, if the cockpit ships them (raw PNG bytes as TextAssets).</summary>
        public const string DriverRes = "Cockpit/wiper_phase_driver", PassengerRes = "Cockpit/wiper_phase_passenger";

        /// <summary>The stroke value s over one stroke: <paramref name="ph"/> 0..1 of the stroke's
        /// time, up then back, eased at both ends (a sine: the linkage's crank). 0 parked.</summary>
        public static float S(float ph)
        {
            if (ph <= 0f || ph >= 1f) return 0f;
            float u = ph < 0.5f ? ph * 2f : 2f - ph * 2f;
            return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * u);
        }

        /// <summary>The modeller's two maps when the cockpit carries them, else null.</summary>
        public static bool TryModelMaps(out float[] driver, out float[] passenger)
        {
            driver = Load(DriverRes);
            passenger = Load(PassengerRes);
            if (driver == null || passenger == null) { driver = passenger = null; return false; }
            return true;
        }

        /// <summary>A map's phases (NaN = never reached), from its PNG bytes, resampled to W x H.</summary>
        static float[] Load(string res)
        {
            var ta = Resources.Load<TextAsset>(res);
            if (ta == null) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!t.LoadImage(ta.bytes, false) || t.width < 2 || t.height < 2) return null;
                var px = t.GetPixels32();
                int tw = t.width, th = t.height;
                var o = new float[W * H];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        // nearest texel: a phase map is not to be blended with "never" here
                        int sx = Mathf.Min(tw - 1, x * tw / W), sy = Mathf.Min(th - 1, y * th / H);
                        byte v = px[sy * tw + sx].r;
                        o[y * W + x] = v == 0 ? float.NaN : (v - 1) / 254f;
                    }
                return o;
            }
            finally { if (Application.isPlaying) Object.Destroy(t); else Object.DestroyImmediate(t); }
        }

        /// <summary>Placeholder maps from the drawn blades (PSX/CabinGlass's _Wiper / _WiperArc,
        /// pane metres): a texel is swept when it lies in a blade's ring between its inner end and
        /// its tip and inside the stroke's angle; its phase is its share of that angle (the arm's
        /// angle is s x the full stroke).</summary>
        public static void Analytic(Vector4 wiper, Vector4 arc, float mu, float mv, out float[] driver, out float[] passenger)
        {
            driver = new float[W * H];
            passenger = new float[W * H];
            float r1 = wiper.w, r0 = wiper.w * arc.z;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float qx = (x + 0.5f) / W * mu, qy = (y + 0.5f) / H * mv;
                    for (int k = 0; k < 2; k++)
                    {
                        float vx = qx - (k == 0 ? wiper.x : wiper.y), vy = qy - wiper.z;
                        float r = Mathf.Sqrt(vx * vx + vy * vy);
                        float sp = (Mathf.Atan2(vy, vx) - arc.x) / arc.y;
                        float p = sp > 0f && sp < 1f && r > r0 && r < r1 ? sp : float.NaN;
                        (k == 0 ? driver : passenger)[y * W + x] = p;
                    }
                }
        }

        /// <summary>The shader's texture from two phase arrays (NaN = never).</summary>
        public static Texture2D Pack(float[] driver, float[] passenger, float mu, float mv, bool readable = false)
        {
            var px = new Color32[W * H];
            float tx = mu / W, ty = mv / H;
            for (int k = 0; k < 2; k++)
            {
                var ph = k == 0 ? driver : passenger;
                var d = SignedDistance(ph, tx, ty);
                var spread = Spread(ph);
                for (int i = 0; i < px.Length; i++)
                {
                    byte p = (byte)Mathf.RoundToInt(Mathf.Clamp01(float.IsNaN(spread[i]) ? 0f : spread[i]) * 255f);
                    byte e = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f + d[i] / (2f * FieldM)) * 255f);
                    if (k == 0) { px[i].r = p; px[i].b = e; }
                    else { px[i].g = p; px[i].a = e; }
                }
            }
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false, true)
            {
                name = "WiperPhase", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 0,
                hideFlags = HideFlags.HideAndDontSave,
            };
            t.SetPixels32(px);
            t.Apply(false, !readable);
            return t;
        }

        /// <summary>The phase carried a few texels out past the swept area (the mean of the swept
        /// neighbours, repeated), so the bilinear filter at the edge reads the edge's phase.</summary>
        static float[] Spread(float[] ph)
        {
            var a = (float[])ph.Clone();
            var b = new float[a.Length];
            for (int pass = 0; pass < 3; pass++)
            {
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int i = y * W + x;
                        if (!float.IsNaN(a[i])) { b[i] = a[i]; continue; }
                        float sum = 0f; int n = 0;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx, yy = y + dy;
                                if (xx < 0 || yy < 0 || xx >= W || yy >= H) continue;
                                float v = a[yy * W + xx];
                                if (!float.IsNaN(v)) { sum += v; n++; }
                            }
                        b[i] = n > 0 ? sum / n : float.NaN;
                    }
                var t = a; a = b; b = t;
            }
            return a;
        }

        /// <summary>Signed distance (pane metres) to the edge of the swept area: positive inside.
        /// Two-pass chamfer with the texel's true width and height.</summary>
        static float[] SignedDistance(float[] ph, float tx, float ty)
        {
            var din = Chamfer(ph, true, tx, ty);    // inside texels: distance to the nearest outside one
            var dout = Chamfer(ph, false, tx, ty);  // outside texels: distance to the nearest inside one
            var d = new float[ph.Length];
            float half = 0.5f * Mathf.Min(tx, ty);
            for (int i = 0; i < d.Length; i++)
                d[i] = !float.IsNaN(ph[i]) ? din[i] - half : -(dout[i] - half);
            return d;
        }

        static float[] Chamfer(float[] ph, bool fromInside, float tx, float ty)
        {
            const float Big = 1e6f;
            float td = Mathf.Sqrt(tx * tx + ty * ty);
            var d = new float[ph.Length];
            for (int i = 0; i < d.Length; i++)
            {
                bool inside = !float.IsNaN(ph[i]);
                // the measured set gets distances; the other set is the target (0)
                d[i] = inside == fromInside ? Big : 0f;
            }
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float v = d[i];
                    if (v == 0f) continue;
                    if (x > 0) v = Mathf.Min(v, d[i - 1] + tx);
                    if (y > 0)
                    {
                        v = Mathf.Min(v, d[i - W] + ty);
                        if (x > 0) v = Mathf.Min(v, d[i - W - 1] + td);
                        if (x < W - 1) v = Mathf.Min(v, d[i - W + 1] + td);
                    }
                    d[i] = v;
                }
            for (int y = H - 1; y >= 0; y--)
                for (int x = W - 1; x >= 0; x--)
                {
                    int i = y * W + x;
                    float v = d[i];
                    if (v == 0f) continue;
                    if (x < W - 1) v = Mathf.Min(v, d[i + 1] + tx);
                    if (y < H - 1)
                    {
                        v = Mathf.Min(v, d[i + W] + ty);
                        if (x < W - 1) v = Mathf.Min(v, d[i + W + 1] + td);
                        if (x > 0) v = Mathf.Min(v, d[i + W - 1] + td);
                    }
                    d[i] = v;
                }
            // a set with no target at all (an empty map): far from everything
            return d;
        }
    }
}
