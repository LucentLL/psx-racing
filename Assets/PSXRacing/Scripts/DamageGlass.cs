using UnityEngine;
using PSXRacing.LifeSim;

namespace PSXRacing
{
    /// <summary>
    /// DAMAGE ROUND 2 (owner on PC48, 2026-11-16: "the broken glass effect is
    /// very poor and does not communicate the damage to me"). WHICH PANE BROKE,
    /// HOW, AND WHERE, from the car's damage rows - nothing new is saved:
    ///   a FRONT hit that crumpled the nose (panel_damage stage 3) cracks the
    ///     windscreen low on the struck side (a chip and a crack running from
    ///     it); with the bar pushed back (bumper_bar stage 2+: the rails band)
    ///     the strike is a spider web;
    ///   a SIDE hit crazes that side's glass (a dented door, stage 2) or
    ///     shatters it (stage 3);
    ///   a REAR hit that crumpled the tail crazes the rear glass, and shatters
    ///     it with the bar pushed back;
    ///   a ROOF row (roof_glass: a brush of the glass, a roll, a drop onto the
    ///     roof): stage 1 a stone chip under 4.5 m/s, else a crack; stage 2 a
    ///     web on the windscreen and every tempered pane crazed; stage 3 the
    ///     web and every tempered pane shattered.
    /// The strike is placed by the incident (its side, and a seed off its
    /// hitId: the same crack every time the car is shown, in the drive and in
    /// every view). The windscreen is laminated (chip / crack / web, still
    /// glass between the cracks); the side and rear glass tempered (crazed
    /// white / a hole with jagged remnants and the cabin through it):
    /// PSXCarGlass.cginc draws them, and a broken lamp's lens in shards.
    /// PSX_DMG_GLASS2=0: the first cut (one contour web on every pane).
    /// </summary>
    public static class DamageGlass
    {
        public static bool On = System.Environment.GetEnvironmentVariable("PSX_DMG_GLASS2") != "0";

        /// <summary>Windscreen states; tempered states.</summary>
        public const float Chip = 1f / 3f, Crack = 2f / 3f, Web = 1f, Crazed = 0.5f, Shattered = 1f;
        /// <summary>A brush of the glass under this is a stone-chip-sized mark.</summary>
        public const float ChipBelowMps = 4.5f;

        static readonly int OnId = Shader.PropertyToID("_PSXGlsOn"), SId = Shader.PropertyToID("_PSXGlsS"),
            WId = Shader.PropertyToID("_PSXGlsW"), XId = Shader.PropertyToID("_PSXGlsX"), LId = Shader.PropertyToID("_PSXGlsL"),
            TId = Shader.PropertyToID("_PSXGlsT"), FId = Shader.PropertyToID("_PSXGlsF");

        /// <summary>0..1 off an incident's id (0 for a row with none: the bench's).</summary>
        public static float Seed(int hit, int salt)
        {
            unchecked
            {
                uint h = (uint)hit * 2654435761u ^ (uint)(salt + 1) * 40503u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                return (h % 10007u) / 10007f;
            }
        }

        static float Frac(float v) => v - Mathf.Floor(v);

        static void Pane(ref DamageLook.Look k, int i, float state, Vector4 at)
        {
            if (state <= k.panes[i]) return;
            k.panes[i] = state;
            if (i == 0) k.hitW = at; else if (i == 1) k.hitX = at; else if (i == 2) k.hitL = at; else k.hitT = at;
        }

        /// <summary>One row into the look's panes and wheels (DamageLook.Read,
        /// beside its own Fold). <paramref name="dv"/> the hit's delta-V (0
        /// unknown), <paramref name="hit"/> its incident.</summary>
        public static void Fold(ref DamageLook.Look k, string id, int stage, int zone, int side, int corner, float dv, int hit)
        {
            if (WheelScuff.Is(id))
            {
                int w = WheelScuff.CornerOf(id);
                if (!WheelScuff.On || w < 0) return;
                float a = Mathf.Clamp01(stage / 3f);
                if (a > k.rash[w]) { k.rash[w] = a; k.rashSeed[w] = Seed(hit, w + 11); }
                return;
            }
            if (!On || stage <= 0) return;
            float seed = Seed(hit, 0);
            if (id == DamageModel.GlassId || (id == DamageModel.PanelId && zone == DamageModel.ZRoof)) { Roof(ref k, stage, dv, seed); return; }
            if (id != DamageModel.PanelId) return;
            int z = zone;
            if (zone == DamageModel.ZCorner && corner >= 0) { z = corner < 2 ? DamageModel.ZFront : DamageModel.ZRear; side = corner % 2 == 0 ? -1 : 1; }
            if (z == DamageModel.ZFront && stage >= 3)
            {
                // low on the struck side; the crack runs toward the far side
                float x = side != 0 ? side * 0.5f : Mathf.Lerp(-0.2f, 0.2f, seed);
                Pane(ref k, 0, Crack, new Vector4(x, 0.16f, seed, x > 0f ? -1f : 1f));
                k.frontGlass = true;
            }
            else if (z == DamageModel.ZRear && stage >= 3)
            {
                Pane(ref k, 1, Crazed, new Vector4(side * 0.5f, 0.22f, seed, 0f));
                k.rearGlass = true;
            }
            else if (z == DamageModel.ZSide && stage >= 2)
            {
                float st = stage >= 3 ? Shattered : Crazed;
                var at = new Vector4(Mathf.Lerp(-0.6f, 0.6f, seed), 0.32f, seed, 0f);
                if (side <= 0) Pane(ref k, 2, st, at);
                if (side >= 0) Pane(ref k, 3, st, at);
            }
        }

        static void Roof(ref DamageLook.Look k, int stage, float dv, float seed)
        {
            float x = Mathf.Lerp(-0.55f, 0.55f, seed);
            var at = new Vector4(x, Mathf.Lerp(0.5f, 0.8f, Frac(seed * 7.3f)), seed, x > 0f ? -1f : 1f);
            Pane(ref k, 0, stage >= 2 ? Web : dv > 0f && dv < ChipBelowMps ? Chip : Crack, at);
            if (stage < 2) return;
            float st = stage >= 3 ? Shattered : Crazed;
            float sx = Mathf.Lerp(-0.5f, 0.5f, Frac(seed * 3.1f));
            Pane(ref k, 2, st, new Vector4(sx, 0.62f, seed, 0f));                       // struck near the roof rail
            Pane(ref k, 3, st, new Vector4(-sx, 0.62f, Frac(seed + 0.5f), 0f));
            Pane(ref k, 1, st, new Vector4(Mathf.Lerp(-0.4f, 0.4f, Frac(seed * 5.7f)), 0.6f, Frac(seed + 0.25f), 0f));
        }

        /// <summary>After every row: a bar pushed back (stage 2+) behind a
        /// crumpled end took the glass further.</summary>
        public static void Bars(ref DamageLook.Look k, bool frontBack, bool rearBack)
        {
            if (!On) return;
            if (frontBack && k.frontGlass && k.panes.x < Web) k.panes.x = Web;
            if (rearBack && k.rearGlass && k.panes.y < Shattered) k.panes.y = Shattered;
        }

        /// <summary>
        /// The cabin a pane's holes show and the panes are placed by, in metres
        /// from the body box's centre: x the belt (the windscreen's foot) above
        /// it, y the front seat backs along it, z the roof above it.
        /// </summary>
        public static Vector4 Cabin(CarModelDef def, float stretchZ, Vector3 half)
        {
            float belt = DamageModel.BeltOf(def, half.y);
            float roof = def != null && def.roofY > 0.5f ? def.roofY - def.colliderCenter.y : half.y;
            roof = Mathf.Clamp(roof, belt + 0.25f, half.y + 0.3f);
            float cowl = def != null && Mathf.Abs(def.cowlZ) > 0.01f ? (def.cowlZ - def.colliderCenter.z) * Mathf.Max(0.5f, stretchZ) : half.z * 0.3f;
            return new Vector4(belt, cowl - 1.10f, roof, 0f);
        }

        /// <summary>The panes into one renderer's block (DamageLook.WriteBlock).</summary>
        public static void Write(MaterialPropertyBlock b, DamageLook.Look now, bool on, Vector3 half, Vector4 cabin)
        {
            bool glass = on && On;
            b.SetFloat(OnId, glass ? 1f : 0f);
            if (!glass) return;
            float belt = cabin.x, seatZ = cabin.y, roof = cabin.z, gh = Mathf.Max(0.2f, roof - belt - 0.05f);
            b.SetVector(SId, now.panes);
            b.SetVector(WId, new Vector4(now.hitW.x * half.x * 0.6f, belt + now.hitW.y * gh, now.hitW.z, now.hitW.w));
            b.SetVector(XId, new Vector4(now.hitX.x * half.x * 0.6f, belt + now.hitX.y * gh, now.hitX.z, 0f));
            // a front door's glass: from the pillar by the cowl back to the seat backs
            b.SetVector(LId, new Vector4(seatZ + 0.55f + now.hitL.x * 0.3f, belt + now.hitL.y * gh, now.hitL.z, 0f));
            b.SetVector(TId, new Vector4(seatZ + 0.55f + now.hitT.x * 0.3f, belt + now.hitT.y * gh, now.hitT.z, 0f));
            b.SetVector(FId, new Vector4(belt, seatZ, roof, half.x));
        }

        // ================= the cockpit's windscreen =================
        /// <summary>The windscreen's state as a level: 0 whole, 1 a chip, 2 a crack, 3 a web.</summary>
        public static int Level(float state) => state > 0.84f ? 3 : state > 0.5f ? 2 : state > 0.1f ? 1 : 0;

        /// <summary>How wide the cockpit's crack quad is for a level (m).</summary>
        public static float QuadMetres(int level) => level <= 1 ? 0.16f : 1.1f;

        /// <summary>
        /// The windscreen's break as the driver sees it, on a square of
        /// <see cref="QuadMetres"/> centred on the strike: thin pale lines, the
        /// road plain between them (no pane goes opaque: a web's crushed centre
        /// is 7 cm). u runs to the car's right.
        /// </summary>
        public static Texture2D CockpitTexture(int level, float seed, float dir)
        {
            int S = level <= 1 ? 128 : 512;
            float m = QuadMetres(level), ppm = S / m;      // pixels per metre
            var px = new Color32[S * S];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(232, 240, 244, 0);
            var rng = new System.Random(1999 + Mathf.RoundToInt(seed * 9973f));
            float Rnd() => (float)rng.NextDouble();
            Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
            void Dot(Vector2 p, byte a)
            {
                int x = Mathf.RoundToInt(p.x), y = Mathf.RoundToInt(p.y);
                if (x < 0 || y < 0 || x >= S || y >= S) return;
                int i = y * S + x;
                if (px[i].a < a) px[i].a = a;
            }
            void Line(Vector2 a, Vector2 b, byte alpha, float wide = 1f)
            {
                float len = (b - a).magnitude;
                int n = Mathf.Max(1, Mathf.CeilToInt(len * 1.5f));
                Vector2 nrm = len > 1e-3f ? new Vector2(-(b - a).y, (b - a).x) / len : Vector2.zero;
                for (int i = 0; i <= n; i++)
                {
                    Vector2 p = Vector2.Lerp(a, b, i / (float)n);
                    Dot(p, alpha);
                    Dot(p + nrm * 0.6f * wide, (byte)(alpha * 0.6f));
                    Dot(p - nrm * 0.6f * wide, (byte)(alpha * 0.6f));
                }
            }
            // a jagged run from a to b
            void Run(Vector2 a, Vector2 b, byte alpha, float jag)
            {
                int n = Mathf.Max(2, Mathf.RoundToInt((b - a).magnitude / (0.035f * ppm)));
                Vector2 d = (b - a) / n, nr = new Vector2(-d.y, d.x).normalized;
                Vector2 p = a;
                for (int i = 1; i <= n; i++)
                {
                    Vector2 q = a + d * i + nr * ((Rnd() - 0.5f) * jag * ppm * (i < n ? 1f : 0f));
                    Line(p, q, alpha);
                    p = q;
                }
            }
            void Disc(float r, byte alpha)
            {
                int ri = Mathf.CeilToInt(r);
                for (int y = -ri; y <= ri; y++)
                    for (int x = -ri; x <= ri; x++)
                    {
                        float dd = Mathf.Sqrt(x * x + y * y);
                        if (dd <= r) Dot(c + new Vector2(x, y), (byte)(alpha * Mathf.Clamp01(1.2f - dd / Mathf.Max(r, 1f))));
                    }
            }
            // the pit and its short legs (every level)
            Disc(0.011f * ppm, 235);
            for (int i = 0; i < 5; i++)
            {
                float a = (i + Rnd() * 0.6f) / 5f * Mathf.PI * 2f, len = Mathf.Lerp(0.03f, 0.062f, Rnd()) * ppm;
                Line(c, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * len, 220);
            }
            Vector2 d1 = new Vector2(dir >= 0f ? 1f : -1f, 0.28f + 0.4f * Rnd()).normalized;
            if (level >= 2)
            {
                // the crack that runs: up and across, wandering
                Vector2 end = c + d1 * Mathf.Min(0.5f, 0.42f + 0.1f * Rnd()) * ppm;
                Run(c, end, 215, 0.03f);
                if (level == 2) Run(c, c + new Vector2(-d1.x * 0.5f, 0.9f).normalized * (0.14f + 0.12f * Rnd()) * ppm, 200, 0.012f);
            }
            if (level >= 3)
            {
                const int N = 15;
                const float R = 0.24f;
                var ang = new float[N];
                var len = new float[N];
                for (int i = 0; i < N; i++)
                {
                    ang[i] = (i + 0.5f + (Rnd() - 0.5f) * 0.55f) / N * Mathf.PI * 2f;
                    len[i] = (Rnd() > 0.78f ? Mathf.Lerp(1.5f, 2.1f, Rnd()) : Mathf.Lerp(0.7f, 1.1f, Rnd())) * R;
                    Run(c, c + new Vector2(Mathf.Cos(ang[i]), Mathf.Sin(ang[i])) * Mathf.Min(len[i], 0.52f) * ppm, 215, 0.012f);
                }
                // the rings: chords between neighbouring rays, closer and more of them near the strike
                for (float r = 0.028f; r < R; r *= 1.42f)
                    for (int i = 0; i < N; i++)
                    {
                        int j = (i + 1) % N;
                        float rr = r * Mathf.Pow(1.42f, 0.5f + (Rnd() - 0.5f) * 0.6f);
                        if (Rnd() > Mathf.Lerp(0.96f, 0.35f, rr / R) || rr > len[i] || rr > len[j]) continue;
                        Line(c + new Vector2(Mathf.Cos(ang[i]), Mathf.Sin(ang[i])) * rr * ppm,
                             c + new Vector2(Mathf.Cos(ang[j]), Mathf.Sin(ang[j])) * rr * ppm, 190);
                    }
                for (int i = 0; i < 34; i++)
                {
                    float a = (i + Rnd()) / 34f * Mathf.PI * 2f;
                    Line(c, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (0.05f + 0.07f * Rnd()) * ppm, 170);
                }
                Disc(0.035f * ppm, 200);   // crushed white, 7 cm: the only part that hides the road
            }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, true)
            {
                name = "GlassBreak" + level, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }
    }
}
