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
    /// B/A the glow-weighted head height, so a deck above the heads or a road
    /// far under them takes none. No slot limit, no new draw call: a phone
    /// pays one fetch a pixel, less than the twelve-lamp loop it replaces for
    /// the street kind. Uptown's heads are white LED, the neighbourhoods'
    /// warm bulbs (the owner's aerial: orange dots on nearly every street).
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
        /// <summary>Uptown (the Square, Trade x Tryon) - inside it the heads are
        /// white LED, outside it warm bulbs; metres, world XZ.</summary>
        public static Vector2 UptownCentre = new Vector2(-2259f, 4782f);
        public static float UptownInnerM = 700f, UptownOuterM = 1300f;
        /// <summary>sRGB: an uptown LED head, a neighbourhood bulb.</summary>
        public static readonly Color FieldWhite = new Color(1.00f, 0.95f, 0.86f);
        public static readonly Color FieldWarm = new Color(1.00f, 0.84f, 0.52f);
        /// <summary>The city's bounce on a tower face at night, linear: the
        /// lit streets' light coming back off the next tower. Ref faces read
        /// Ycode 19-45 against a near-black sky; ours read 4-7.</summary>
        public static Vector3 CityBounce = new Vector3(0.075f, 0.069f, 0.063f);

        public static bool FieldEnabled = System.Environment.GetEnvironmentVariable("PSX_LAMPFIELD") != "0";
        public static bool FieldActive { get; private set; }
        public static int FieldLamps { get; private set; }

        static int fieldVersion;
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
        static void PushField(Vector3 eye)
        {
            if (!FieldEnabled) { FieldOff(); return; }
            float span = FieldRes * FieldTexelM;
            var origin = new Vector2(Mathf.Round(eye.x / FieldSnapM) * FieldSnapM - span * 0.5f,
                                     Mathf.Round(eye.z / FieldSnapM) * FieldSnapM - span * 0.5f);
            if (origin != builtOrigin || builtVersion != fieldVersion || builtStreetOn != StreetOn)
            {
                builtOrigin = origin;
                builtVersion = fieldVersion;
                builtStreetOn = StreetOn;
                builtBaseY = Mathf.Round(eye.y);
                FieldLamps = StreetOn ? BuildField(origin, builtBaseY) : 0;
            }
            FieldActive = FieldLamps > 0;
            if (!FieldActive) { FieldOff(); return; }
            Shader.SetGlobalTexture(FieldTexId, fieldTex);
            Shader.SetGlobalVector(FieldSTId, new Vector4(origin.x, origin.y, 1f / span, 1f));
            Shader.SetGlobalVector(FieldYId, new Vector4(builtBaseY, UptownCentre.x, UptownCentre.y, 1f / Mathf.Max(1f, UptownOuterM - UptownInnerM)));
            var w = FieldWhite.linear; var k = FieldWarm.linear;
            Shader.SetGlobalVector(FieldWhiteId, new Vector4(w.r, w.g, w.b, UptownOuterM));
            Shader.SetGlobalVector(FieldWarmId, new Vector4(k.r, k.g, k.b, 0f));
            Shader.SetGlobalVector(BounceId, new Vector4(CityBounce.x, CityBounce.y, CityBounce.z, 1f));
        }

        static void FieldOff()
        {
            FieldActive = false;
            Shader.SetGlobalVector(FieldSTId, Vector4.zero);
            Shader.SetGlobalVector(BounceId, Vector4.zero);
        }

        /// <summary>Splat every marked, lit lamp whose glow reaches the window.</summary>
        static int BuildField(Vector2 origin, float baseY)
        {
            int n4 = FieldRes * FieldRes * 4;
            if (acc == null || acc.Length != n4) { acc = new float[n4]; halfs = new ushort[n4]; }
            System.Array.Clear(acc, 0, n4);
            if (fieldTex == null)
            {
                fieldTex = new Texture2D(FieldRes, FieldRes, TextureFormat.RGBAHalf, false, true)
                {
                    name = "PSXLampField", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave,
                };
            }
            float inv = 1f / FieldTexelM;
            float rp = FieldPoolM, rg = FieldGlowM;
            int reach = Mathf.CeilToInt(Mathf.Max(rp, rg) * inv);
            int lamps = 0;
            for (int i = 0; i < high; i++)
            {
                ref Entry e = ref entries[i];
                if (!e.used || !e.field || !e.on || e.kind != Kind.Street || e.intensity <= 0f) continue;
                if (e.owned && e.owner == null) continue;
                float cx = (e.pos.x - origin.x) * inv - 0.5f, cz = (e.pos.z - origin.y) * inv - 0.5f;
                if (cx < -reach || cz < -reach || cx > FieldRes + reach || cz > FieldRes + reach) continue;
                lamps++;
                float pool = e.intensity * FieldPoolGain, glow = e.intensity * FieldGlowGain;
                float dy = e.pos.y - baseY;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - reach)), x1 = Mathf.Min(FieldRes - 1, Mathf.CeilToInt(cx + reach));
                int z0 = Mathf.Max(0, Mathf.FloorToInt(cz - reach)), z1 = Mathf.Min(FieldRes - 1, Mathf.CeilToInt(cz + reach));
                float irp2 = 1f / (rp * rp), irg2 = 1f / (rg * rg);
                for (int z = z0; z <= z1; z++)
                {
                    float dz = (z - cz) * FieldTexelM;
                    int row = z * FieldRes;
                    for (int x = x0; x <= x1; x++)
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
                        acc[o + 2] += g * dy;
                        acc[o + 3] += g;
                    }
                }
            }
            for (int i = 0; i < n4; i++) halfs[i] = Mathf.FloatToHalf(acc[i]);
            fieldTex.SetPixelData(halfs, 0);
            fieldTex.Apply(false, false);
            return lamps;
        }
    }
}
