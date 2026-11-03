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
        public const int CityTrafficCap = 96;
        public const int HDMaxLines = 2160;
        public const int ProbeFacesPerFrame = 2;
        public const int ProbeFaceRes = 512;

        static int pc = -1, maxTraffic = -1;

        /// <summary>The PC profile is on.</summary>
        public static bool PC
        {
            get { if (pc < 0) pc = Decide() ? 1 : 0; return pc == 1; }
        }

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

        /// <summary>One line for the Player.log.</summary>
        public static string Describe() => PC
            ? $"PC profile: city ring {CityRing} ({(2 * CityRing + 1) * 256} m square), traffic {(MaxTraffic ? "RUSH HOUR / cap " + CityTrafficCap : "by the hour")}, HD to {HDMaxLines} lines, reflections {ProbeFacesPerFrame} faces/frame at {ProbeFaceRes}"
            : "web profile";
    }
}
