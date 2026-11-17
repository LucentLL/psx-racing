using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// RAIN 2 (2026-11-16; the owner: "I'd like to see the rain effects be
    /// improved. This means while sitting idle, cruising, and racing. Splashing
    /// on the ground, how it pools on the road. I say this, meanwhile we need
    /// to start focusing HEAVILY on performance.").
    ///
    /// The switches, the quality tier and the two numbers every part reads -
    /// how hard it is raining right now and how much water stands on the road.
    /// The parts:
    /// <list type="bullet">
    /// <item>THE GROUND: pools where a road can hold water and the rings the
    /// rain throws on it, per wet pixel (PSXRain2.cginc, in PSX/Lit's two wet
    /// passes); the crowns and rings of drops landing round the camera, on
    /// ground that was probed (<see cref="Rain2Driver"/>), none under a deck
    /// or a roof.</item>
    /// <item>THE GLASS: wipers on the cockpit's windscreen, the beads building
    /// up again behind each sweep (PSX/CabinGlass, CockpitInterior).</item>
    /// <item>THE CARS: a tyre through a pool throws a sheet and a burst; the
    /// spray grows into a rooster tail at racing speed (TireSpray); each tyre
    /// leaves a damp strip that closes over again (<see cref="WetTrail"/>).</item>
    /// <item>THE SOUND: rain on the road and on the roof, tyre hiss on wet
    /// tarmac, the hit of a puddle (<see cref="Rain2Audio"/>, all made in
    /// code: there is no rain recording in the game).</item>
    /// </list>
    ///
    /// PSX_RAIN2=0 is the rain as it was (every part off; the shaders evaluate
    /// nothing new). Each part has its own switch as well, a public static so
    /// the perf tour's A/B can flip it (ab=Rain2.Rings:0). Everything is
    /// pooled or in a shader: no object per drop, nothing allocated per frame.
    /// </summary>
    public static class Rain2
    {
        public enum Tier { Mobile = 0, Low = 1, High = 2 }

        /// <summary>The whole package. PSX_RAIN2=0: the rain as it was.</summary>
        public static bool On = Env("PSX_RAIN2", true);
        /// <summary>Standing water by slope + the gutter pans (PSX_RAIN2_POOLS).</summary>
        public static bool Pools = Env("PSX_RAIN2_POOLS", true);
        /// <summary>The drops' rings in the water (PSX_RAIN2_RINGS).</summary>
        public static bool Rings = Env("PSX_RAIN2_RINGS", true);
        /// <summary>The crowns and lit rings of drops landing near the camera (PSX_RAIN2_SPLASH).</summary>
        public static bool Splashes = Env("PSX_RAIN2_SPLASH", true);
        /// <summary>The cockpit's wipers (PSX_RAIN2_WIPERS).</summary>
        public static bool Wipers = Env("PSX_RAIN2_WIPERS", true);
        /// <summary>The glass's heavier beading and its lamps-in-the-drops (PSX_RAIN2_GLASS).</summary>
        public static bool Glass = Env("PSX_RAIN2_GLASS", true);
        /// <summary>A tyre through a pool, and the rooster tail (PSX_RAIN2_SPRAY).</summary>
        public static bool Spray = Env("PSX_RAIN2_SPRAY", true);
        /// <summary>The damp strip a tyre leaves behind it on a wet road (PSX_RAIN2_TRAILS).</summary>
        public static bool Trails = Env("PSX_RAIN2_TRAILS", true);
        /// <summary>The rain's own sound (PSX_RAIN2_AUDIO).</summary>
        public static bool Audio = Env("PSX_RAIN2_AUDIO", true);

        static bool Env(string name, bool fallback)
        {
            string v = System.Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(v)) return fallback;
            return v != "0";
        }

        static int tierCached = -1;
        /// <summary>PC with REFLECTIONS on is HIGH, a PC without (or a desktop
        /// browser) LOW, a phone MOBILE. PSX_RAIN2_TIER=high|low|mobile forces it.</summary>
        public static Tier CurrentTier
        {
            get
            {
                if (tierCached < 0)
                {
                    string v = System.Environment.GetEnvironmentVariable("PSX_RAIN2_TIER");
                    if (!string.IsNullOrEmpty(v))
                    {
                        v = v.ToLowerInvariant();
                        tierCached = v.StartsWith("h") ? 2 : v.StartsWith("l") ? 1 : v.StartsWith("m") ? 0 : -2;
                    }
                    else tierCached = -2;
                }
                if (tierCached >= 0) return (Tier)tierCached;
                if (Application.isMobilePlatform) return Tier.Mobile;
                return PlatformProfile.PC && ReflectionPrefs.On ? Tier.High : Tier.Low;
            }
        }
        /// <summary>Tools: force a tier (or -1 for the platform's).</summary>
        public static void ForceTier(int tier) { tierCached = tier < 0 ? -2 : Mathf.Clamp(tier, 0, 2); }

        // ---- the play check's meter ---------------------------------------
        /// <summary>The play check turns this on: the three per-frame parts
        /// (the driver, the audio, every car's spray) then add their own time
        /// and managed allocation here. Off (the game): two branches a frame.</summary>
        public static bool Measure;
        public static long DriverTicks, DriverMaxTicks, DriverAlloc, AudioTicks, AudioAlloc, SprayTicks, SprayMaxTicks, SprayAlloc;
        public static int DriverFrames, SprayCalls;
        /// <summary>The managed allocation counter the meter reads: the thread's
        /// own count where the runtime keeps one, else the heap in use (the
        /// editor's Mono keeps none; a collection between two reads then shows
        /// as a drop, which <see cref="AllocSince"/> counts as nothing).</summary>
        public static long AllocNow()
        {
            long a = System.GC.GetAllocatedBytesForCurrentThread();
            return a != 0 ? a : System.GC.GetTotalMemory(false);
        }
        public static long AllocSince(long was) { long d = AllocNow() - was; return d > 0 ? d : 0; }

        public static void MeterReset()
        {
            DriverTicks = DriverMaxTicks = DriverAlloc = AudioTicks = AudioAlloc = SprayTicks = SprayMaxTicks = SprayAlloc = 0;
            DriverFrames = SprayCalls = 0;
        }

        // ---- what the shaders read ----------------------------------------
        static readonly int R2Id = Shader.PropertyToID("_PSXRain2");
        static readonly int CoverId = Shader.PropertyToID("_PSXRain2Cover");
        public static readonly int CoverTexId = Shader.PropertyToID("_PSXRain2CoverTex");
        public static readonly int PoolBandId = Shader.PropertyToID("_PoolBand");

        /// <summary>How hard it is raining on the open road right now, 0..1
        /// (0 when nothing falls), and how much water stands on it, 0..1 - as
        /// last pushed.</summary>
        public static float RainNow { get; private set; }
        public static float PoolLevel { get; private set; }

        /// <summary>The standing water a road shows for what the weather says
        /// lies on it: filling while it rains (never ahead of the road's own
        /// water), and the last thing to dry afterwards.</summary>
        public static float LevelFor(bool raining, float precipRate, float roadWater)
        {
            roadWater = Mathf.Clamp01(roadWater);
            if (raining) return Mathf.Min(0.6f + 0.4f * Mathf.Clamp01(precipRate), 0.25f + roadWater);
            return 0.8f * roadWater;
        }

        /// <summary>Set the shaders' rain: <paramref name="rain"/> falling now
        /// 0..1, <paramref name="level"/> standing water 0..1. The game's
        /// driver calls it every frame; a shot tool calls it once.</summary>
        public static void Push(float rain, float level)
        {
            RainNow = On ? Mathf.Clamp01(rain) : 0f;
            PoolLevel = On && Pools ? Mathf.Clamp01(level) : 0f;
            float layers = !On || !Rings ? 0f : CurrentTier == Tier.High ? 2f : CurrentTier == Tier.Low ? 1f : 0f;
            // x is the shaders' one gate: with no pools there is no film/pool
            // split either, and the rings ride the old picture's water
            Shader.SetGlobalVector(R2Id, new Vector4(On && (PoolLevel > 0.001f || (layers > 0f && RainNow > 0.01f)) ? 1f : 0f,
                                                     RainNow, PoolLevel, layers));
        }

        /// <summary>The cover mask (Rain2Driver): world metres to its uv, or off.</summary>
        public static void PushCover(Texture mask, float metresAcross)
        {
            if (mask == null) { Shader.SetGlobalVector(CoverId, Vector4.zero); return; }
            Shader.SetGlobalTexture(CoverTexId, mask);
            Shader.SetGlobalVector(CoverId, new Vector4(1f / metresAcross, 0f, 1f, 0f));
        }

        // ---- the pool, on the CPU (PSXRain2.cginc's PSXRain2At, part 1) ---
        const float SlopeLo = 0.008f, SlopeHi = 0.070f, Shed = 0.40f, DipA = 0.26f, DipB = 0.61f;
        const float LineDry = 0.86f, LineFull = 0.60f, Edge = 0.035f;

        /// <summary>How much water stands at world point <paramref name="p"/>
        /// of a surface with normal <paramref name="n"/>, 0..1: the tyres'
        /// puddle. The shader's rule, line for line (keep them in step); the
        /// gutter pans are not in it (a tyre is not in the gutter).</summary>
        public static float PoolAt(Vector3 p, Vector3 n)
        {
            float level = PoolLevel;
            if (level <= 0.001f || n.y * 4f - 2.8f <= 0f) return 0f;
            float slope = Mathf.Sqrt(n.x * n.x + n.z * n.z) / Mathf.Max(n.y, 0.25f);
            float shed = RoadSurface.SmoothStep01(SlopeLo, SlopeHi, slope);
            float nz = RoadSurface.Noise(p.x * DipA + 31.7f, p.z * DipA + 31.7f) * 0.58f
                     + RoadSurface.Noise(p.x * DipB + 7.3f, p.z * DipB + 7.3f) * 0.42f;
            float line = Mathf.Lerp(LineDry, LineFull, level) + shed * Shed;
            return RoadSurface.SmoothStep01(line, line + Edge, nz);
        }

        // ---- the gutter pans ------------------------------------------------
        /// <summary>The city's structure atlas: the band its gutter pans are
        /// drawn from (CityMeshes.Curbs2 UvPanGrey 0.270..0.360; the light pan
        /// of PSX_CITY_PANTONE=0 is 0.900..0.975 and is not pooled).</summary>
        static readonly Vector4 PanBand = new Vector4(0.268f, 0.362f, 1f, 0f);
        static MaterialPropertyBlock panBlock;

        /// <summary>Called by WetReflections.Tag for every wet renderer: the
        /// city's structure mesh (pans, lids, grates) is told which of its
        /// atlas bands is the gutter, so its water stands and runs.</summary>
        public static void TagRenderer(Renderer r, Material[] mats)
        {
            if (!On || !Pools || r == null || mats == null) return;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || !m.name.StartsWith("CityStructure", System.StringComparison.Ordinal)) continue;
                if (panBlock == null) panBlock = new MaterialPropertyBlock();
                r.GetPropertyBlock(panBlock);
                panBlock.SetVector(PoolBandId, PanBand);
                r.SetPropertyBlock(panBlock);
                return;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // domain reload off: nothing of the last session's rain survives
            RainNow = 0f; PoolLevel = 0f;
            Shader.SetGlobalVector(R2Id, Vector4.zero);
            Shader.SetGlobalVector(CoverId, Vector4.zero);
        }
    }
}
