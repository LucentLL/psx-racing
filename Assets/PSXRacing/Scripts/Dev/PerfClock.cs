using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine.LowLevel;

namespace PSXRacing
{
    /// <summary>
    /// CITY PERF ROUND 1 (2026-10-30; owner: "in the city FPS is about 59 and
    /// dropped as low as 3"): a frame's time in the PLAYER, split the way the
    /// player loop spends it, and the city's own work inside it.
    ///
    /// OFF unless <see cref="PerfTour"/> (a player served from this machine
    /// with ?perftour) installs it. Installed, a marker system stands before
    /// every system of every top-level player-loop phase (and one at each
    /// phase's end), so each system's share of the frame is known, plus the
    /// time the frame spent OUTSIDE the loop (the browser, a frame-end
    /// collection); and the city's builders add their own slices through
    /// <see cref="Add"/>. Off, a hook in the game's code is one branch on
    /// <see cref="On"/> and nothing else.
    /// </summary>
    public static class PerfClock
    {
        /// <summary>The clock is installed (a perf tour is running).</summary>
        public static bool On { get; private set; }

        /// <summary>The city's work, timed where it happens.</summary>
        public enum Slot
        {
            TileStep, TileFinish, TileSync, TileStandUp, TileAbandon, TileDrop,
            TreeStep, TreeUrgent, TreeStand, Signs,
            ProxyStep, ProxyRefresh, FarLand, FarTrees, FarPrefetch, FarLights, FarOther, Skyline, FadeTick,
            TrafficUpdate, TrafficFixed, LampField,
            // (the late scripts that render a view of their own: CarReflections' paint faces, the HUD's minimap)
            CarReflect, Minimap,
            COUNT
        }

        public static readonly string[] SlotNames =
        {
            "tile step", "tile finish", "tile sync build", "tile stand-up", "tile abandon", "tile drop",
            "tree step", "tree urgent", "tree stand", "signs",
            "proxy step", "proxy refresh", "far land", "far trees", "far prefetch", "far lights", "far other", "skyline", "fade tick",
            "traffic update", "traffic fixed", "lamp field",
            "car reflections", "minimap",
        };

        static readonly double ToMs = 1000.0 / Stopwatch.Frequency;
        public static long Now() => Stopwatch.GetTimestamp();
        public static double MsSince(long t0) => (Stopwatch.GetTimestamp() - t0) * ToMs;

        /// <summary>This frame's city slices, ms.</summary>
        public static readonly double[] Slots = new double[(int)Slot.COUNT];
        public static void Add(Slot s, long t0) => Slots[(int)s] += (Stopwatch.GetTimestamp() - t0) * ToMs;
        public static void AddMs(Slot s, double ms) => Slots[(int)s] += ms;

        // ---- the player loop ----
        /// <summary>Every top-level system as "Phase.System", in loop order.</summary>
        public static string[] SystemNames = new string[0];
        /// <summary>This frame's ms per system (a FixedUpdate system sums its steps).</summary>
        public static double[] SystemMs = new double[0];
        public static int[] SystemCalls = new int[0];
        /// <summary>The fixed steps this frame (calls of the first FixedUpdate system).</summary>
        public static int FixedSteps => fixedFirst >= 0 && fixedFirst < SystemCalls.Length ? SystemCalls[fixedFirst] : 0;
        static int fixedFirst = -1;

        /// <summary>Called at the start of every frame for the frame before it,
        /// with its whole time and the part spent outside the player loop, while
        /// <see cref="SystemMs"/> and <see cref="Slots"/> still hold its numbers.</summary>
        public static System.Action<double, double> FrameDone;

        static long lastT, frameT0, endT;
        static int open = -1;
        static bool frameStarted;

        struct Marker { }

        /// <summary>Put the markers into the player loop (once).</summary>
        public static void Install()
        {
            if (On) return;
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            var phases = loop.subSystemList;
            if (phases == null || phases.Length == 0) return;
            var names = new List<string>();
            bool first = true;
            for (int p = 0; p < phases.Length; p++)
            {
                var subs = phases[p].subSystemList ?? new PlayerLoopSystem[0];
                string pn = phases[p].type != null ? phases[p].type.Name : "Phase" + p;
                var list = new List<PlayerLoopSystem>(subs.Length * 2 + 1);
                for (int i = 0; i < subs.Length; i++)
                {
                    int key = names.Count;
                    names.Add(pn + "." + (subs[i].type != null ? subs[i].type.Name : "#" + i));
                    if (pn == "FixedUpdate" && fixedFirst < 0) fixedFirst = key;
                    bool fs = first;
                    first = false;
                    list.Add(new PlayerLoopSystem { type = typeof(Marker), updateDelegate = () => Mark(key, fs, false) });
                    list.Add(subs[i]);
                }
                bool last = p == phases.Length - 1;
                list.Add(new PlayerLoopSystem { type = typeof(Marker), updateDelegate = () => Mark(-1, false, last) });
                phases[p].subSystemList = list.ToArray();
            }
            loop.subSystemList = phases;
            SystemNames = names.ToArray();
            SystemMs = new double[SystemNames.Length];
            SystemCalls = new int[SystemNames.Length];
            PlayerLoop.SetPlayerLoop(loop);
            On = true;
        }

        static void Mark(int key, bool frameStart, bool loopEnd)
        {
            long t = Stopwatch.GetTimestamp();
            if (frameStart)
            {
                if (frameStarted && FrameDone != null)
                {
                    try { FrameDone((t - frameT0) * ToMs, endT > frameT0 ? (t - endT) * ToMs : 0.0); }
                    catch (System.Exception e) { UnityEngine.Debug.LogException(e); }
                }
                System.Array.Clear(SystemMs, 0, SystemMs.Length);
                System.Array.Clear(SystemCalls, 0, SystemCalls.Length);
                System.Array.Clear(Slots, 0, Slots.Length);
                frameT0 = t;
                frameStarted = true;
            }
            else if (open >= 0) SystemMs[open] += (t - lastT) * ToMs;
            if (loopEnd) endT = t;
            open = key;
            if (key >= 0) SystemCalls[key]++;
            lastT = t;
        }
    }
}
