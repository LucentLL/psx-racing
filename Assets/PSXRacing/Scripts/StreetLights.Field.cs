using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE LIT CITY STREET (the city-night pass, 2026-10-05). The owner, after
    /// driving Charlotte at night: "The roads and cars are too dark at night on
    /// the main roads", with five uptown night photographs - mid-grey asphalt
    /// lit evenly by repeated warm-white pools on both kerbs, the paint and
    /// the cars readable under them.
    ///
    /// Measured on our frames first (tools/colour/colour_stats.py citynight):
    /// the uptown road read Ycode 4-25 against the photographs' 90-141, the
    /// cars 14-20 against 36-111. The table of twelve slots lit only the eight
    /// lamps nearest the eye - with 12-13 lamps standing within 80 m and 37-53
    /// within 160 m of every uptown spot - and an 18 m pool of a 9 m head lights
    /// barely 10 m of road round its foot, so a lit street was a few pools and
    /// long black gaps under the owner's dark-night ambient.
    ///
    /// So a lit city street's lamps (those the Charlotte tiles mark, see
    /// <see cref="MarkField"/>) do not take slots: every one within 250 m of
    /// the eye is splatted, top-down, into one small camera-centred texture -
    /// the LAMP FIELD - and every PSX surface reads it with one texture fetch
    /// (PSXLamps.cginc, PSXLampField): R the pool (a 28 m windowed disc round
    /// the post), G a wide low glow (40 m: the light the street, the shop
    /// glass and the kerbs scatter onto car sides and the lower storeys),
    /// B/G the glow-weighted head height, so a deck above the heads or a road
    /// far under them takes none, A the MERCURY-VAPOUR heads' part of G (the
    /// 1990s city, 2026-10-29: by street). No slot limit, no new draw call: a
    /// phone pays one fetch a pixel, less than the twelve-lamp loop it
    /// replaces for the street kind. Every head is a warm bulb (the owner's
    /// aerial: orange dots on nearly every street) or, on some streets, a
    /// mercury-vapour head; uptown's were white LED until the 1990s city
    /// (2026-10-29: "LEDs were not common") - its heads keep their light
    /// (<see cref="UptownGain"/>) in the bulb's colour.
    ///
    /// Only the city marks lamps, so every other venue - the Town, the
    /// mountain and rural roads - never turns the field on and keeps the
    /// owner's dark night exactly (psx-racing-dark-night). PSX_LAMPFIELD=0
    /// (editor) turns it off for an A/B.
    /// </summary>
    public static partial class StreetLights
    {
        public const int FieldRes = 256;          // texels a side
        public const float FieldTexelM = 2f;      // 512 m across
        const float FieldSnapM = 32f;             // the window moves in these steps
        /// <summary>The pool's horizontal radius on the street (m).</summary>
        public static float FieldPoolM = 28f;
        /// <summary>The scattered glow's radius (m).</summary>
        public static float FieldGlowM = 40f;
        /// <summary>The pool per lamp, x the lamp's own intensity (2.0 x gain).</summary>
        public static float FieldPoolGain = 1.8f;
        /// <summary>The glow per lamp, x the lamp's own intensity.</summary>
        public static float FieldGlowGain = 0.30f;
        /// <summary>Uptown (the Square, Trade x Tryon) - inside it the heads
        /// give <see cref="UptownGain"/> the light; metres, world XZ.</summary>
        public static Vector2 UptownCentre = new Vector2(-2259f, 4782f);
        // (night v3, 2026-10-06: 700/1300 -> 1000/1500 - E Trade St at
        // McDowell, 1 km out, read half-bulb at sat 0.24 against the refs' 0.05.)
        public static float UptownInnerM = 1000f, UptownOuterM = 1500f;
        /// <summary>sRGB: the white LED head uptown had until 2026-10-29 (only
        /// its luminance is used now: <see cref="UptownGain"/>), a bulb. A bulb
        /// is warm, not sodium: (1, 0.84, 0.52) over the asphalt read as sand
        /// (the correction round; PSX/Lit reads the lit asphalt grey).</summary>
        public static readonly Color FieldWhite = new Color(1.00f, 0.98f, 0.94f);
        public static readonly Color FieldWarm = new Color(1.00f, 0.90f, 0.72f);
        /// <summary>Uptown's heads x this: the old LED white's linear luminance
        /// over the bulb's (.959 / .810), so uptown's streets keep the light
        /// they were measured at (night v3) in the 1990s colour.</summary>
        public static float UptownGain => FieldLum(FieldWhite.linear) / FieldLum(FieldWarm.linear);
        /// <summary>A mercury-vapour head on the field: <see cref="Mercury"/>
        /// at the bulb's (FieldWarm's) linear luminance, linear.</summary>
        static Color FieldMercury => Mercury.linear * (FieldLum(FieldWarm.linear) / FieldLum(Mercury.linear));
        static float FieldLum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        /// <summary>The city's bounce on a tower face at night, linear: the
        /// lit streets' light coming back off the next tower. Ref faces read
        /// Ycode 19-45 against a near-black sky; ours read 4-7.</summary>
        public static Vector3 CityBounce = new Vector3(0.075f, 0.069f, 0.063f);

        public static bool FieldEnabled = System.Environment.GetEnvironmentVariable("PSX_LAMPFIELD") != "0";
        public static bool FieldActive { get; private set; }
        public static int FieldLamps { get; private set; }
        /// <summary>Of <see cref="FieldLamps"/>, the mercury-vapour heads (the 1990s city).</summary>
        public static int FieldMercuryLamps { get; private set; }

        static int fieldVersion;

        // ---- city-build-hitches (2026-10-29): the field rebuilt only when it changes ----
        /// <summary>Every tile stood up or dropped, every tree frame's lamps,
        /// bumped the field's version and rebuilt it (5-11 ms in the editor,
        /// two or three times a second at speed at night), though a tile's
        /// lamps come and go at the ring's edge, half a kilometre out, past
        /// the window's 256 m and the glow's 40 m. A lamp change now counts
        /// only where that lamp's splat meets the window last built (the very
        /// test BuildField skips a lamp by), so the window holds what a rebuild
        /// would have drawn. (Not bit for bit: a rebuild also re-based the
        /// head heights on the eye's height then; the 32 m steps still do.)
        /// PSX_LAMPFIELD_CULL=0: every change rebuilds, as before.</summary>
        public static bool FieldCull = System.Environment.GetEnvironmentVariable("PSX_LAMPFIELD_CULL") != "0";

        /// <summary>Does a lamp at <paramref name="pos"/> splat into the window
        /// last built (BuildField's own reach test)? True before any build.</summary>
        static bool OnFieldWindow(Vector3 pos)
        {
            if (float.IsNaN(builtOrigin.x)) return true;
            float inv = 1f / FieldTexelM;
            int reach = Mathf.CeilToInt(Mathf.Max(FieldPoolM, FieldGlowM) * inv);
            float cx = (pos.x - builtOrigin.x) * inv - 0.5f, cz = (pos.z - builtOrigin.y) * inv - 0.5f;
            return !(cx < -reach || cz < -reach || cx > FieldRes + reach || cz > FieldRes + reach);
        }
        static int builtVersion = -1;
        static Vector2 builtOrigin = new Vector2(float.NaN, float.NaN);
        static float builtBaseY;
        static bool builtStreetOn;
        static Texture2D fieldTex;
        static float[] acc;
        static ushort[] halfs;

        static readonly int FieldTexId = Shader.PropertyToID("_PSXLampField");
        static readonly int FieldSTId = Shader.PropertyToID("_PSXLampFieldST");
        static readonly int FieldYId = Shader.PropertyToID("_PSXLampFieldY");
        static readonly int FieldWarmId = Shader.PropertyToID("_PSXFieldWarm");
        static readonly int FieldWhiteId = Shader.PropertyToID("_PSXFieldWhite");
        static readonly int BounceId = Shader.PropertyToID("_PSXCityBounce");

        /// <summary>The field for an eye: rebuilt when the window steps or a
        /// marked lamp changes, else only re-sent (the stale-global rule:
        /// every push writes the switch).</summary>
        /// <summary>The field's rebuild, for the profiler (tools\perf-probe.ps1).</summary>
        static readonly Unity.Profiling.ProfilerMarker BuildFieldMarker = new Unity.Profiling.ProfilerMarker("StreetLights.BuildField");

        /// <param name="mayRebuild">False for a secondary view in play (a
        /// mirror, the car-reflection probe): it draws with the window the
        /// game's view last built, never its own. Its eye is metres from the
        /// driver's, and the 512 m window holds all it shows; but whenever the
        /// two eyes fell either side of a <see cref="FieldSnapM"/> step the
        /// field was rebuilt for each in turn, two to four times a frame - a
        /// splat, 262k half conversions and a 512 KB upload each (the perf
        /// probe, 2026-10-25: 5-12 ms frames inside the probe's render).</param>
        static void PushField(Vector3 eye, bool mayRebuild = true)
        {
            if (!FieldEnabled) { FieldOff(); return; }
            float span = FieldRes * FieldTexelM;
            var origin = new Vector2(Mathf.Round(eye.x / FieldSnapM) * FieldSnapM - span * 0.5f,
                                     Mathf.Round(eye.z / FieldSnapM) * FieldSnapM - span * 0.5f);
            if (!mayRebuild && fieldTex != null && builtStreetOn == StreetOn) origin = builtOrigin;
            else if (origin != builtOrigin || builtVersion != fieldVersion || builtStreetOn != StreetOn)
            {
                long pc = PerfClock.On ? PerfClock.Now() : 0;   // city perf round 1
                // city perf round 1: a step of a window already shown is built
                // over frames behind it (FieldSliced); the first window, a
                // street-lights switch and the editor's tools build it whole
                if (FieldSliced && Application.isPlaying && fieldTex != null && builtStreetOn == StreetOn && StreetOn)
                {
                    using (BuildFieldMarker.Auto()) StepPending(origin, Mathf.Round(eye.y));
                    origin = builtOrigin;
                }
                else
                {
                    pendOn = false;
                    builtOrigin = origin;
                    builtVersion = fieldVersion;
                    builtStreetOn = StreetOn;
                    builtBaseY = Mathf.Round(eye.y);
                    using (BuildFieldMarker.Auto())
                        FieldLamps = StreetOn ? BuildField(origin, builtBaseY) : 0;
                }
                if (PerfClock.On) PerfClock.Add(PerfClock.Slot.LampField, pc);
            }
            FieldActive = FieldLamps > 0;
            if (!FieldActive) { FieldOff(); return; }
            Shader.SetGlobalTexture(FieldTexId, fieldTex);
            Shader.SetGlobalVector(FieldSTId, new Vector4(origin.x, origin.y, 1f / span, 1f));
            Shader.SetGlobalVector(FieldYId, new Vector4(builtBaseY, UptownCentre.x, UptownCentre.y, 1f / Mathf.Max(1f, UptownOuterM - UptownInnerM)));
            // (the 1990s city: _PSXFieldWhite carries the mercury head now,
            // _PSXFieldWarm.w uptown's gain)
            var w = FieldMercury; var k = FieldWarm.linear;
            Shader.SetGlobalVector(FieldWhiteId, new Vector4(w.r, w.g, w.b, UptownOuterM));
            Shader.SetGlobalVector(FieldWarmId, new Vector4(k.r, k.g, k.b, UptownGain));
            Shader.SetGlobalVector(BounceId, new Vector4(CityBounce.x, CityBounce.y, CityBounce.z, 1f));
        }

        static void FieldOff()
        {
            FieldActive = false;
            Shader.SetGlobalVector(FieldSTId, Vector4.zero);
            Shader.SetGlobalVector(BounceId, Vector4.zero);
        }

        static Texture2D NewFieldTex() => new Texture2D(FieldRes, FieldRes, TextureFormat.RGBAHalf, false, true)
        {
            name = "PSXLampField", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave,
        };

        /// <summary>Splat every marked, lit lamp whose glow reaches the window.</summary>
        static int BuildField(Vector2 origin, float baseY)
        {
            int n4 = FieldRes * FieldRes * 4;
            if (acc == null || acc.Length != n4) { acc = new float[n4]; halfs = new ushort[n4]; }
            System.Array.Clear(acc, 0, n4);
            if (fieldTex == null) fieldTex = NewFieldTex();
            int lamps = 0, mercs = 0;
            SplatLamps(origin, baseY, 0, high, ref lamps, ref mercs, double.MaxValue);
            ConvertRows(0, FieldRes);
            fieldTex.SetPixelData(halfs, 0);
            fieldTex.Apply(false, false);
            FieldMercuryLamps = mercs;
            return lamps;
        }

        // ---- city perf round 1 (2026-10-30): the window's step built over frames ----
        /// <summary>A rebuild - the window's 32 m step, about once a second at
        /// 100 km/h: a splat of every lamp in reach, 262k half conversions and a
        /// 512 KB upload - took 9-10 ms in ONE frame in the editor (the perf
        /// tour), two to three times that in a browser. A step of a window
        /// already shown is now built behind it into a second texture, a slice
        /// a frame (<see cref="SliceMs"/>), the shown one drawn until the new one
        /// is whole and then the two swap: the eye is never more than a step
        /// from the shown window's centre, and its 512 m holds the 40 m glows
        /// of everything round it. The same splat, the same field (a lamp
        /// change mid-build shows on the next one). PSX_LAMPFIELD_SLICE=0 (or
        /// ?ab=StreetLights.FieldSliced:0): whole, as before.</summary>
        public static bool FieldSliced = System.Environment.GetEnvironmentVariable("PSX_LAMPFIELD_SLICE") != "0";
        const double SliceMs = 1.5;
        static Texture2D fieldBack;
        static bool pendOn;
        static int pendStage, pendLamp, pendRow, pendLamps, pendMercs, pendVersion, pendFrame = -1;
        static Vector2 pendOrigin;
        static float pendBaseY;

        /// <summary>One frame's share of the window being built for
        /// <paramref name="origin"/>; the swap when it is done.</summary>
        static void StepPending(Vector2 origin, float baseY)
        {
            if (pendFrame == Time.frameCount) return;   // a frame's share, whatever the cameras
            pendFrame = Time.frameCount;
            int n4 = FieldRes * FieldRes * 4;
            // a build is for the window the eye is in now; a step on starts it again
            if (!pendOn || pendOrigin != origin)
            {
                pendOn = true; pendStage = 0; pendLamp = 0; pendRow = 0; pendLamps = 0; pendMercs = 0;
                pendOrigin = origin; pendBaseY = baseY; pendVersion = fieldVersion;
                if (acc == null || acc.Length != n4) { acc = new float[n4]; halfs = new ushort[n4]; }
            }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (pendStage == 0)
            {
                // the clear, in rows (a 1 MB clear is itself a millisecond in a browser)
                int rows = FieldRes / 4;
                System.Array.Clear(acc, pendRow * FieldRes * 4, rows * FieldRes * 4);
                pendRow += rows;
                if (pendRow < FieldRes) return;
                pendRow = 0; pendStage = 1;
                if (clock.Elapsed.TotalMilliseconds >= SliceMs) return;
            }
            if (pendStage == 1)
            {
                pendLamp = SplatLamps(pendOrigin, pendBaseY, pendLamp, high, ref pendLamps, ref pendMercs, SliceMs - clock.Elapsed.TotalMilliseconds);
                if (pendLamp < high) return;
                pendStage = 2;
                if (clock.Elapsed.TotalMilliseconds >= SliceMs) return;
            }
            if (pendStage == 2)
            {
                while (pendRow < FieldRes && clock.Elapsed.TotalMilliseconds < SliceMs)
                {
                    int r1 = Mathf.Min(FieldRes, pendRow + 32);
                    ConvertRows(pendRow, r1);
                    pendRow = r1;
                }
                if (pendRow < FieldRes) return;
                pendStage = 3;
                return;   // the upload on a frame of its own
            }
            // the upload, and the swap
            if (fieldBack == null) fieldBack = NewFieldTex();
            fieldBack.SetPixelData(halfs, 0);
            fieldBack.Apply(false, false);
            (fieldTex, fieldBack) = (fieldBack, fieldTex);
            builtOrigin = pendOrigin;
            builtBaseY = pendBaseY;
            builtVersion = pendVersion;
            builtStreetOn = StreetOn;
            FieldLamps = pendLamps;
            FieldMercuryLamps = pendMercs;
            pendOn = false;
        }

        /// <summary>Convert accumulator rows [r0, r1) to halves.</summary>
        static void ConvertRows(int r0, int r1)
        {
            int i0 = r0 * FieldRes * 4, i1 = r1 * FieldRes * 4;
            // (an untouched texel is +0, whose half is 0: no call for it)
            if (FieldCull) for (int i = i0; i < i1; i++) halfs[i] = acc[i] == 0f ? (ushort)0 : Mathf.FloatToHalf(acc[i]);
            else for (int i = i0; i < i1; i++) halfs[i] = Mathf.FloatToHalf(acc[i]);
        }

        /// <summary>Splat lamps [<paramref name="from"/>, <paramref name="to"/>)
        /// into the accumulator for a window, until <paramref name="budgetMs"/>
        /// is spent; returns the next lamp to splat.</summary>
        static int SplatLamps(Vector2 origin, float baseY, int from, int to, ref int lamps, ref int mercs, double budgetMs)
        {
            float inv = 1f / FieldTexelM;
            float rp = FieldPoolM, rg = FieldGlowM;
            int reach = Mathf.CeilToInt(Mathf.Max(rp, rg) * inv);
            var clock = budgetMs < double.MaxValue ? System.Diagnostics.Stopwatch.StartNew() : null;
            int i = from;
            for (; i < to; i++)
            {
                if (clock != null && ((i - from) & 15) == 15 && clock.Elapsed.TotalMilliseconds >= budgetMs) break;
                ref Entry e = ref entries[i];
                if (!e.used || !e.field || !e.on || e.kind != Kind.Street || e.intensity <= 0f) continue;
                if (e.owned && e.owner == null) continue;
                float cx = (e.pos.x - origin.x) * inv - 0.5f, cz = (e.pos.z - origin.y) * inv - 0.5f;
                if (cx < -reach || cz < -reach || cx > FieldRes + reach || cz > FieldRes + reach) continue;
                lamps++;
                if (e.mercury) mercs++;
                float pool = e.intensity * FieldPoolGain, glow = e.intensity * FieldGlowGain;
                float dy = e.pos.y - baseY, merc = e.mercury ? glow : 0f;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - reach)), x1 = Mathf.Min(FieldRes - 1, Mathf.CeilToInt(cx + reach));
                int z0 = Mathf.Max(0, Mathf.FloorToInt(cz - reach)), z1 = Mathf.Min(FieldRes - 1, Mathf.CeilToInt(cz + reach));
                float irp2 = 1f / (rp * rp), irg2 = 1f / (rg * rg);
                for (int z = z0; z <= z1; z++)
                {
                    float dz = (z - cz) * FieldTexelM;
                    int row = z * FieldRes;
                    // city-build-hitches: only the texels of the row the glow's
                    // disc can reach (one texel of margin; the test below still
                    // decides every one of them, so the field is the same)
                    int xa = x0, xb = x1;
                    if (FieldCull)
                    {
                        float rm = Mathf.Max(rp, rg) + FieldTexelM, dd = rm * rm - dz * dz;
                        if (dd < 0f) continue;
                        float hx = Mathf.Sqrt(dd) * inv + 1f;
                        xa = Mathf.Max(x0, Mathf.FloorToInt(cx - hx)); xb = Mathf.Min(x1, Mathf.CeilToInt(cx + hx));
                    }
                    for (int x = xa; x <= xb; x++)
                    {
                        float dx = (x - cx) * FieldTexelM;
                        float d2 = dx * dx + dz * dz;
                        float g = 1f - d2 * irg2;
                        if (g <= 0f) continue;
                        g *= g;
                        float p = 1f - d2 * irp2;
                        p = p > 0f ? p * p : 0f;
                        int o = (row + x) * 4;
                        acc[o] += pool * p;
                        acc[o + 1] += glow * g;
                        // B/G: the head height weighted by the glow; A: the
                        // mercury heads' part of the glow (the 1990s city)
                        acc[o + 2] += glow * g * dy;
                        acc[o + 3] += merc * g;
                    }
                }
            }
            return i;
        }
    }
}
