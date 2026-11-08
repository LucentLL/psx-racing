using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// How often each secondary view redraws (VISUALS), chosen by the player.
    /// Owner 2026-11-08: "I don't like that the side mirrors have different
    /// frame rate than the rearview. It is very distracting. allow player to
    /// select their mirror (drivers, passenger, rearview) FPS. Same with
    /// players car, other racer cars, traffic, and city reflection fps."
    ///
    /// AUTO is the shared one-extra-view-a-frame schedule (ExtraViews): the
    /// rear-view every 2nd frame, a door mirror every 4th, the paint's
    /// panorama on what is left - cheapest, and why the mirrors disagreed.
    /// FULL is every frame; a number is redraws a second, from a time
    /// accumulator so it holds whatever the game's own frame rate. The mirrors
    /// default to 30 each, matched.
    ///
    /// CAR REFLECTIONS is the panorama every car's paint reads - the player's
    /// car, the other racers and the traffic all show the same one (it is
    /// captured round the player's car), so one rate serves all three. The
    /// wet road and the city's glass reflect in screen space every frame and
    /// have no rate of their own.
    /// </summary>
    public static class ViewRatePrefs
    {
        public enum View { RearMirror = 0, DriverMirror = 1, PassengerMirror = 2, CarReflections = 3 }

        /// <summary>The choices, in cycle order: 0 AUTO, -1 FULL, else per second.</summary>
        static readonly int[] Steps = { 0, -1, 60, 30, 20, 15, 10 };
        static readonly string[] Keys = { "psx.rate.rear", "psx.rate.drivermirror", "psx.rate.passmirror", "psx.rate.carrefl" };
        static readonly int[] Defaults = { 30, 30, 30, 0 };
        static readonly int[] cached = { int.MinValue, int.MinValue, int.MinValue, int.MinValue };
        static readonly float[] acc = new float[4];
        static readonly int[] tickedAt = { -1, -1, -1, -1 };
        static readonly bool[] dueNow = new bool[4];

        /// <summary>0 AUTO, -1 FULL, else redraws a second.</summary>
        public static int Rate(View v)
        {
            int i = (int)v;
            if (cached[i] == int.MinValue) cached[i] = PlayerPrefs.GetInt(Keys[i], Defaults[i]);
            return cached[i];
        }

        public static bool Auto(View v) => Rate(v) == 0;

        public static void Cycle(View v)
        {
            int cur = Rate(v), k = System.Array.IndexOf(Steps, cur);
            int next = Steps[(k + 1 + Steps.Length) % Steps.Length];
            cached[(int)v] = next;
            acc[(int)v] = 0f;
            PlayerPrefs.SetInt(Keys[(int)v], next);
            PlayerPrefs.Save();
        }

        public static string Label(View v)
        {
            int r = Rate(v);
            return r == 0 ? "AUTO" : r < 0 ? "FULL" : r + " FPS";
        }

        /// <summary>Should view <paramref name="v"/> redraw this frame (a set
        /// rate, not AUTO)? Asked any number of times a frame, it answers the
        /// same; the accumulator steps once a frame.</summary>
        public static bool Due(View v)
        {
            int i = (int)v, r = Rate(v);
            if (r <= 0) return true;
            int f = Time.frameCount;
            if (tickedAt[i] != f)
            {
                tickedAt[i] = f;
                acc[i] += Time.unscaledDeltaTime * r;
                dueNow[i] = acc[i] >= 1f;
                if (dueNow[i]) acc[i] = Mathf.Min(acc[i] - 1f, 1f);
            }
            return dueNow[i];
        }

        /// <summary>Faces of the six-face panorama to draw this frame at a set
        /// rate (FULL: all six, a new panorama every frame).</summary>
        public static int ProbeFaces()
        {
            int r = Rate(View.CarReflections);
            if (r < 0) return 6;
            int f = Time.frameCount, i = (int)View.CarReflections;
            if (tickedAt[i] != f)
            {
                tickedAt[i] = f;
                acc[i] = Mathf.Min(acc[i] + Time.unscaledDeltaTime * r * 6f, 12f);
            }
            int n = Mathf.Min(6, Mathf.FloorToInt(acc[i]));
            acc[i] -= n;
            return n;
        }
    }
}
