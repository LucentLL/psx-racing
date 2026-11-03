using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE PC PROFILE (owner, 2026-11-02: "a proper unity build that I can
    /// test on PC to judge real performance... full screen, max resolution,
    /// max draw distance, maximum traffic... It should represent progress
    /// towards a final, releasable build"). A native desktop player (the
    /// Windows standalone) has every core and a real GPU, so it runs what the
    /// web build cannot afford:
    ///   * the streamed city <see cref="CityRing"/> tiles out each way (9 x 9
    ///     of 256 m: a kilometre to the edge of the built city, where the web
    ///     build keeps 5 x 5), and the camera's far plane out to it;
    ///   * the city's traffic cap at <see cref="CityTrafficCap"/>, and every
    ///     race and the free roam at RUSH HOUR unless the race page picks
    ///     another level (<see cref="MaxTraffic"/>);
    ///   * HD at the display's own lines up to <see cref="HDMaxLines"/> (4K);
    ///   * the car-reflection panorama <see cref="ProbeFacesPerFrame"/> faces
    ///     a frame at <see cref="ProbeFaceRes"/>;
    ///   * full screen at the desktop's resolution (PSXBuildWin64).
    /// The web build, phones and the editor are untouched.
    ///
    /// For an A/B in the same player:
    ///   PSXRacing.exe -psx-web-profile       everything as the web build has it
    ///   PSXRacing.exe -psx-traffic-by-hour   traffic by the clock again
    /// PSX_PROFILE=PC (or WEB) forces it either way, the editor included.
    /// </summary>
    public static class PlatformProfile
    {
        public const int CityRing = 2;   // ring 4 (2026-11-02) outran the main-thread tile builder on I-277: the bigger view waits for baked, streamed tiles
        /// <summary>The ring with the baked city open (CityTileCache.On): 9 x 9 tiles, a kilometre each way.</summary>
        public const int CityRingBaked = 4;
        public const int CityTrafficCap = 96;
        public const int HDMaxLines = 2160;
        public const int ProbeFacesPerFrame = 1;   // 2 cost +1.6 ms a frame on the Uptown tour (2026-11-02)
        public const int ProbeFaceRes = 512;
        /// <summary>The sun-shadow box (SunShadows): half its side and how far
        /// up the road it is centred, metres, and the two maps' texels.</summary>
        public const float ShadowHalfExtentM = 180f, ShadowAheadM = 120f;
        public const int ShadowRes = 4096, SkyShadowRes = 1024;
        /// <summary>URP's SSAO, as the PC profile runs it: after the opaques
        /// (our surfaces are hand-written unlit passes that never sample it
        /// in their lighting), from depth alone (they write no normals).</summary>
        public const float AOIntensity = 0.85f, AORadius = 0.6f;

        static int pc = -1, maxTraffic = -1;

        /// <summary>The PC profile is on.</summary>
        public static bool PC
        {
            get { if (pc < 0) pc = Decide() ? 1 : 0; return pc == 1; }
        }

        /// <summary>The PC tier's sun shadows (bigger, sharper box); -psx-small-shadows for the A/B.</summary>
        public static bool PCShadows => PC && !HasArg("-psx-small-shadows");
        /// <summary>The PC tier's ambient occlusion; -psx-no-ao for the A/B.</summary>
        public static bool PCAO => PC && !HasArg("-psx-no-ao");
        /// <summary>
        /// THE PC TIER'S PICTURE at HD (2026-11-03, the PS3-PS4 target: "PS1
        /// look stays as an optional CLASSIC mode; the new look is the
        /// default"): the display blit keeps all 8 bits a channel instead of
        /// the PS1's 6 (whose steps, dithered, laid horizontal bands down every
        /// sunset sky), and smooths edges with FXAA (PSX/Blit). Every other
        /// preset, the web build and the editor are untouched;
        /// -psx-ps1-picture is the A/B.
        /// </summary>
        public static bool PCPicture => PC && !HasArg("-psx-ps1-picture");

        /// <summary>Traffic at its most wherever nobody picked a level.</summary>
        public static bool MaxTraffic
        {
            get { if (maxTraffic < 0) maxTraffic = PC && !HasArg("-psx-traffic-by-hour") ? 1 : 0; return maxTraffic == 1; }
        }

        static bool Decide()
        {
            string env = System.Environment.GetEnvironmentVariable("PSX_PROFILE");
            if (string.Equals(env, "PC", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(env, "WEB", System.StringComparison.OrdinalIgnoreCase)) return false;
            if (Application.isEditor || Application.isMobilePlatform || Application.platform == RuntimePlatform.WebGLPlayer) return false;
            return !HasArg("-psx-web-profile");
        }

        static bool HasArg(string a)
        {
            foreach (var s in System.Environment.GetCommandLineArgs())
                if (string.Equals(s, a, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void LogIt()
        {
            pc = -1; maxTraffic = -1;
            Debug.Log("[PlatformProfile] " + Describe());
        }

        /// <summary>
        /// THE PC TIER'S AMBIENT OCCLUSION. The PC renderer carries URP's SSAO,
        /// set to read depth AND normals during lighting - which our world never
        /// gives it: PSX/Lit is a hand-written unlit pass with no DepthNormals
        /// pass, so the occlusion only ever reached the cars. In the PC profile
        /// it runs AFTER the opaques from depth alone, so the ground darkens
        /// where trees, walls, kerbs and buildings meet it. The renderer asset
        /// is changed in memory only, in the player only (never the editor, where
        /// it would be saved).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ApplyAO()
        {
            if (!PCAO || Application.isEditor) return;
            try
            {
                var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
                if (urp == null) return;
                var list = typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)
                    .GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                    .GetValue(urp) as UnityEngine.Rendering.Universal.ScriptableRendererData[];
                if (list == null) return;
                int done = 0;
                foreach (var data in list)
                {
                    if (data == null) continue;
                    foreach (var f in data.rendererFeatures)
                    {
                        if (f == null || f.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                        var sf = f.GetType().GetField("m_Settings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var s = sf?.GetValue(f);
                        if (s == null) continue;
                        var t = s.GetType();
                        void Set(string name, object v) { var fi = t.GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance); if (fi != null) fi.SetValue(s, fi.FieldType.IsEnum ? System.Enum.ToObject(fi.FieldType, v) : v); }
                        Set("AfterOpaque", true);
                        Set("Source", 0);            // depth
                        Set("Intensity", AOIntensity);
                        Set("Radius", AORadius);
                        Set("Downsample", false);
                        sf.SetValue(f, s);
                        f.SetActive(true);
                        done++;
                    }
                    data.SetDirty();
                }
                Debug.Log($"[PlatformProfile] PC ambient occlusion: {done} SSAO feature(s) after opaques, depth only, intensity {AOIntensity}, radius {AORadius} m");
            }
            catch (System.Exception e) { Debug.LogWarning("[PlatformProfile] PC ambient occlusion not set: " + e.Message); }
        }

        /// <summary>One line for the Player.log.</summary>
        public static string Describe() => PC
            ? $"PC profile: city ring {CityRing} ({(2 * CityRing + 1) * 256} m square), traffic {(MaxTraffic ? "RUSH HOUR / cap " + CityTrafficCap : "by the hour")}, HD to {HDMaxLines} lines, reflections {ProbeFacesPerFrame} faces/frame at {ProbeFaceRes}, sun shadows {(PCShadows ? $"{ShadowHalfExtentM * 2:0} m at {ShadowRes}" : "as web")}, AO {(PCAO ? "after opaques" : "off")}, picture {(PCPicture ? "8-bit + FXAA at HD" : "as web")}"
            : "web profile";
    }
}
