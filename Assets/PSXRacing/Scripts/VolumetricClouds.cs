using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE VOLUMETRIC CLOUDS' runtime half (2026-10-25; the owner, of the
    /// dynamic sky: "Those clouds need to be overhauled because they look
    /// strange and too repetitive" - he chose real volumetric clouds). The
    /// picture is Shaders/PSXVolClouds.cginc; this keeps its DOME MAP full:
    ///
    ///   * ONCE: the noise (Resources/Clouds/*.bytes, made by
    ///     tools/sky/gen_cloud_noise.py) into a 128^3 and a 64^3 Texture3D and
    ///     a 256^2 weather map; two 2048 x 512 half-float maps and a compact
    ///     512 x 128 target.
    ///   * ON THE HOUR / THE WEATHER (<see cref="Configure"/>, from
    ///     DynamicSky.Apply): the day's clouds out of Seasons.Now's SKY layer -
    ///     clear = a few small fair-weather cumulus or none, and some days
    ///     cirrus; partly cloudy = broken cumulus, towering on some days;
    ///     overcast = a full grey deck; storm = a thick dark deck with towers
    ///     in it - varied day to day by a hash of the date, and the wind.
    ///   * EVERY FRAME (<see cref="Tick"/>, DynamicSky's driver): one Blit
    ///     marches one map texel in every 4 x 4 block, one Blit reprojects
    ///     the rest by the eye's move and the wind. No camera, no readback, no
    ///     allocation: a handful of SetVector calls on the CPU.
    ///
    /// Phones (and a GPU without 3D textures or half-float targets) keep the
    /// old puffs: <see cref="Wanted"/>.
    /// </summary>
    public static class VolumetricClouds
    {
        public const int MapW = 2048, MapH = 512;
        public const string ShaderName = "Hidden/PSX/VolClouds";
        /// <summary>View steps a march takes (the shader's VOL_MAX_STEPS caps it).</summary>
        public const int Steps = 48;
        /// <summary>The 4 x 4 Bayer order: the texel of each block marched on
        /// frame k. Successive frames land far apart in the block.</summary>
        static readonly Vector2Int[] Order =
        {
            new Vector2Int(0, 0), new Vector2Int(2, 2), new Vector2Int(2, 0), new Vector2Int(0, 2),
            new Vector2Int(1, 1), new Vector2Int(3, 3), new Vector2Int(3, 1), new Vector2Int(1, 3),
            new Vector2Int(1, 0), new Vector2Int(3, 2), new Vector2Int(3, 0), new Vector2Int(1, 2),
            new Vector2Int(0, 1), new Vector2Int(2, 3), new Vector2Int(2, 1), new Vector2Int(0, 3),
        };
        /// <summary>How fast the cloud shapes climb through their noise (m/s):
        /// the slow boil of a growing cumulus.</summary>
        const float RiseSpeed = 0.7f;

        /// <summary>Tools and the perf probe's A/B: force on (true) or off
        /// (false, the old puffs). Null = <see cref="Wanted"/>'s own answer.</summary>
        public static bool? Override;

        /// <summary>Desktop, unless PSX_VOLCLOUDS=0 says otherwise.</summary>
        public static bool Wanted
        {
            get
            {
                if (Override.HasValue) return Override.Value;
                string e = System.Environment.GetEnvironmentVariable("PSX_VOLCLOUDS");
                if (e == "0") return false;
                if (e == "1") return true;
                return !Application.isMobilePlatform;
            }
        }

        /// <summary>The volumetric clouds are what the dynamic sky draws now.</summary>
        public static bool Active { get; private set; }

        /// <summary>The perf probe's A/B: true freezes the map (no march, no
        /// reproject) so the two Blits' GPU cost can be told apart.</summary>
        public static bool Paused;

        /// <summary>The perf probe's: the CPU this costs, accumulated.</summary>
        public static double TickMsTotal;
        public static int TickCount;

        static Material mat;
        static Texture3D shape, detail;
        static Texture2D weather;
        static RenderTexture mapA, mapB, compact;
        static bool aCurrent, failed;
        static Vector2 windVel, wind;
        static float rise;
        static int frame, sinceReset;
        static bool reset = true, haveEye;
        static Vector3 lastEye;

        static readonly int idOn = Shader.PropertyToID("_PSXVolOn");
        static readonly int idCov = Shader.PropertyToID("_PSXVolCov");
        static readonly int idSlab = Shader.PropertyToID("_PSXVolSlab");
        static readonly int idLight = Shader.PropertyToID("_PSXVolLightDir");
        static readonly int idEye = Shader.PropertyToID("_PSXVolEye");
        static readonly int idMap = Shader.PropertyToID("_PSXVolMap");
        static readonly int idWind = Shader.PropertyToID("_PSXVolWind");
        static readonly int idSteps = Shader.PropertyToID("_PSXVolSteps");
        static readonly int idPhase = Shader.PropertyToID("_PSXVolPhase");
        static readonly int idSize = Shader.PropertyToID("_PSXVolSize");
        static readonly int idMove = Shader.PropertyToID("_PSXVolMove");

        /// <summary>Can this machine run them at all.</summary>
        public static bool Supported
        {
            get
            {
                if (failed) return false;
                if (!SystemInfo.supports3DTextures) return false;
                if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)) return false;
                if (!SystemInfo.SupportsTextureFormat(TextureFormat.R8)) return false;
                var sh = Shader.Find(ShaderName);
                return sh != null && sh.isSupported;
            }
        }

        static bool Ensure()
        {
            if (failed) return false;
            if (mat == null)
            {
                var sh = Shader.Find(ShaderName);
                if (sh == null || !sh.isSupported) { failed = true; Debug.LogWarning("VolumetricClouds: " + ShaderName + " missing or unsupported - the old clouds"); return false; }
                mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (shape == null)
            {
                shape = Load3D("Clouds/CloudShape", 128, "VolCloudShape");
                detail = Load3D("Clouds/CloudDetail", 64, "VolCloudDetail");
                var ta = Resources.Load<TextAsset>("Clouds/CloudWeather");
                if (shape == null || detail == null || ta == null) { failed = true; Debug.LogWarning("VolumetricClouds: noise missing - the old clouds"); return false; }
                weather = new Texture2D(256, 256, TextureFormat.RGBA32, false, true)
                {
                    wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave, name = "VolCloudWeather",
                };
                weather.SetPixelData(ta.GetData<byte>(), 0);
                weather.Apply(false, true);
                Resources.UnloadAsset(ta);
                mat.SetTexture("_PSXVolShape", shape);
                mat.SetTexture("_PSXVolDetail", detail);
                mat.SetTexture("_PSXVolWeather", weather);
            }
            if (mapA == null || !mapA.IsCreated() || mapB == null || !mapB.IsCreated() || compact == null || !compact.IsCreated())
            {
                Release();
                mapA = MakeRT(MapW, MapH, "VolCloudMapA");
                mapB = MakeRT(MapW, MapH, "VolCloudMapB");
                compact = MakeRT(MapW / 4, MapH / 4, "VolCloudFresh");
                mat.SetTexture("_PSXVolFresh", compact);
                mat.SetVector(idSize, new Vector4(MapW, MapH, 1f / MapW, 1f / MapH));
                reset = true;
            }
            return true;
        }

        static Texture3D Load3D(string path, int n, string name)
        {
            var ta = Resources.Load<TextAsset>(path);
            if (ta == null) return null;
            var t = new Texture3D(n, n, n, TextureFormat.R8, false)
            {
                wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave, name = name,
            };
            t.SetPixelData(ta.GetData<byte>(), 0);
            t.Apply(false, true);
            Resources.UnloadAsset(ta);
            return t;
        }

        static RenderTexture MakeRT(int w, int h, string name)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                useMipMap = false, autoGenerateMips = false, filterMode = FilterMode.Bilinear,
                wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave, name = name,
            };
            rt.Create();
            return rt;
        }

        static void Release()
        {
            if (mapA != null) { mapA.Release(); Object.DestroyImmediate(mapA); mapA = null; }
            if (mapB != null) { mapB.Release(); Object.DestroyImmediate(mapB); mapB = null; }
            if (compact != null) { compact.Release(); Object.DestroyImmediate(compact); compact = null; }
        }

        // ---- the day's clouds ---------------------------------------------

        static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16;
            return x;
        }

        /// <summary>0..1, the same for a day and a salt every time.</summary>
        static float H(int day, int salt) => (Hash((uint)(day * 7919 + salt * 104729 + 17)) & 0xFFFFFF) / 16777216f;

        /// <summary>The clouds for a sky, as the shader's four globals: Cov
        /// (coverage, kind bias, overcast deck, storm), Slab (base m, tallest
        /// cumulus m, deck thickness m, cirrus), and the wind (m/s).</summary>
        public static void CloudsFor(in WeatherNow w, out Vector4 cov, out Vector4 slab, out Vector2 windMs)
        {
            int day = w.day;
            string seed = System.Environment.GetEnvironmentVariable("PSX_CLOUD_DAY");
            if (!string.IsNullOrEmpty(seed) && int.TryParse(seed, out int sd)) day = sd;
            float h1 = H(day, 1), h2 = H(day, 2), h3 = H(day, 3), h4 = H(day, 4), h5 = H(day, 5);
            float c, kind, overcast = 0f, storm = 0f, baseM, tallM, deckM = 0f, cirrus = 0f;
            switch (w.sky)
            {
                case SkyKind.Clear:
                    // A few small fair-weather cumulus - or none - and on
                    // some days a sheet of cirrus streaks.
                    c = h1 < 0.3f ? 0.03f : 0.08f + 0.14f * h1;
                    kind = 0.05f + 0.2f * h2;
                    baseM = 1500f + 400f * h3;
                    tallM = 700f + 600f * h2;
                    cirrus = h4 > 0.35f ? 0.35f + 0.65f * h5 : 0f;
                    break;
                case SkyKind.PartlyCloudy:
                    // Broken cumulus; on some days they tower.
                    c = 0.30f + 0.22f * h1;
                    kind = 0.3f + 0.6f * h2;
                    baseM = 1250f + 400f * h3;
                    tallM = 1800f + 2700f * h2;
                    cirrus = h4 > 0.6f ? 0.5f * h5 : 0f;
                    break;
                case SkyKind.Overcast:
                    // A full grey deck, lumpy underneath.
                    c = 0.55f + 0.2f * h1;
                    kind = 0.15f + 0.2f * h2;
                    baseM = 800f + 500f * h3;
                    tallM = 1200f;
                    deckM = 450f + 550f * h1;
                    overcast = 1f;
                    break;
                default:
                    // Storm: a thick dark deck, towers standing in it.
                    c = 0.8f;
                    kind = 0.85f + 0.15f * h2;
                    baseM = 650f + 250f * h3;
                    tallM = 5000f;
                    deckM = 1400f + 1000f * h1;
                    overcast = 1f;
                    storm = 1f;
                    break;
            }
            // Rain falling darkens a deck; fog brings it down onto the hills.
            if (w.precip != PrecipKind.None) storm = Mathf.Max(storm, 0.35f * Mathf.Clamp01(w.precipRate));
            if (w.fog)
            {
                baseM = Mathf.Min(baseM, 600f);
                overcast = Mathf.Max(overcast, 0.8f);
                deckM = Mathf.Max(deckM, 500f);
            }
            cov = new Vector4(c, kind, overcast, storm);
            slab = new Vector4(baseM, tallM, deckM, cirrus);
            float ang = H(day, 6) * Mathf.PI * 2f;
            float speed = 5f + 9f * H(day, 7) + 8f * storm;
            windMs = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * speed;
        }

        /// <summary>
        /// Set up for this hour's light and this day's sky, and fill the map
        /// (from the compact march: soft for the sixteen frames it takes the
        /// march to sharpen it). False = not running (the old puffs draw).
        /// </summary>
        public static bool Configure(in WeatherNow w, Vector3 lightDir)
        {
            if (!Wanted || !Ensure())
            {
                Off();
                return false;
            }
            CloudsFor(w, out var cov, out var slab, out windVel);
            Shader.SetGlobalVector(idCov, cov);
            Shader.SetGlobalVector(idSlab, slab);
            Shader.SetGlobalVector(idLight, lightDir.normalized);
            Shader.SetGlobalFloat(idOn, 1f);
            Active = true;
            reset = true;
            Step(EyeNow(), 0f, true);
            return true;
        }

        /// <summary>The old puffs again (the dynamic sky's fallback), and the
        /// maps' memory back.</summary>
        public static void Off()
        {
            Active = false;
            Shader.SetGlobalFloat(idOn, 0f);
        }

        /// <summary>The photograph is up: nothing reads the maps.</summary>
        public static void Stop()
        {
            Off();
            Release();
        }

        static Vector3 EyeNow()
        {
            var cam = Camera.main;
            return cam != null ? cam.transform.position : (haveEye ? lastEye : Vector3.zero);
        }

        /// <summary>One frame of the map: DynamicSky's driver, every frame.</summary>
        public static void Tick()
        {
            if (!Active || Paused) return;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!Ensure()) { Off(); return; }
            Step(EyeNow(), Time.deltaTime, true);
            TickMsTotal += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            TickCount++;
        }

        /// <summary>
        /// A still frame for a tool (edit mode has no frames): the map from
        /// <paramref name="eye"/>, marched as the game would converge it -
        /// two full turns of the 4 x 4 order with the clock stopped.
        /// </summary>
        public static void Prime(Vector3 eye)
        {
            if (!Active || !Ensure()) return;
            reset = true;
            for (int i = 0; i < 48; i++) Step(eye, 0f, true);
        }

        static void Step(Vector3 eye, float dt, bool jitter)
        {
            if (!haveEye) { lastEye = eye; haveEye = true; reset = true; }
            Vector3 move = eye - lastEye;
            if (move.sqrMagnitude > 250f * 250f) { reset = true; move = Vector3.zero; }
            Vector2 dw = windVel * dt;
            wind += dw;
            rise += RiseSpeed * dt;
            frame++;
            var ph = Order[frame & 15];
            mat.SetVector(idWind, new Vector4(wind.x, wind.y, rise, frame & 1023));
            mat.SetVector(idSteps, new Vector4(Steps, 30f, 18000f, jitter ? 1f : 0f));
            // A fresh texel replaces its history for the first turn after a
            // reset, then keeps a third of each new march (~5 samples).
            mat.SetVector(idPhase, new Vector4(ph.x, ph.y, 0f, sinceReset < 16 ? 1f : 0.35f));
            // The clouds drift against the wind offset (the shader reads the
            // field at xz + wind), so last frame's cloud is a wind step back.
            mat.SetVector(idMove, new Vector4(move.x + dw.x, 0f, move.z + dw.y, reset ? 1f : 0f));
            Shader.SetGlobalVector(idEye, new Vector4(eye.x, eye.y, eye.z, 0f));
            Graphics.Blit(null, compact, mat, 0);
            var src = aCurrent ? mapA : mapB;
            var dst = aCurrent ? mapB : mapA;
            Graphics.Blit(src, dst, mat, 1);
            aCurrent = !aCurrent;
            Shader.SetGlobalTexture(idMap, dst);
            sinceReset = reset ? 0 : sinceReset + 1;
            reset = false;
            lastEye = eye;
        }
    }
}
