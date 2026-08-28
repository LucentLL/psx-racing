using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Which way the look axis goes, remembered across sessions.
    ///
    /// Not a preference this game gets to have an opinion about: a player who
    /// flies inverted has flown inverted since before this game existed, and
    /// pitching the wrong way makes a first-person mode unusable rather than
    /// merely unfamiliar. It applies to every way of looking around on foot —
    /// mouse, right stick, and the thumb drag on a phone — because they are all
    /// the same gesture and a setting that fixed only one of them would read as
    /// broken on whatever device it missed.
    ///
    /// Same shape as <see cref="PSXQuality"/>: a lazily-read PlayerPrefs int
    /// with an eager Save, because on WebGL a preference that is not flushed is
    /// a preference lost to the next tab close.
    /// </summary>
    public static class LookPrefs
    {
        const string PrefKey = "psx.invertY";

        static int cached = -1;

        public static bool InvertY
        {
            get
            {
                if (cached < 0) cached = PlayerPrefs.GetInt(PrefKey, 0);
                return cached != 0;
            }
            set
            {
                int v = value ? 1 : 0;
                if (cached == v) return;
                cached = v;
                PlayerPrefs.SetInt(PrefKey, v);
                PlayerPrefs.Save();
            }
        }

        /// <summary>+1 normal, -1 inverted. Multiply raw pitch deltas by this
        /// at the ONE place they are summed, so a new input source cannot
        /// forget to honour the setting.</summary>
        public static float PitchSign => InvertY ? -1f : 1f;

        public static void Toggle() => InvertY = !InvertY;

        public static string Label => InvertY ? "INVERTED" : "NORMAL";
    }
}
