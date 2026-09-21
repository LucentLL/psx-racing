using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// How hard the PS1 filter is applied — framebuffer lines, colour depth and
    /// dither, as one choice.
    ///
    /// The three are one setting because they are one effect. A PlayStation put
    /// 240 lines of 15-bit colour on a CRT that blurred the dither back into a
    /// gradient before it reached your eye; the same 240 lines of 15-bit colour
    /// on a 2000-pixel-wide phone LCD is eight display pixels per source pixel
    /// with a 4x4 Bayer pattern crawling over every flat surface, which reads as
    /// a DOS-era PC monitor rather than as a console. That was the report:
    /// "resolution is very low and unnecessarily grainy".
    ///
    /// What actually makes this game look like a PS1 is elsewhere and is not
    /// touched by any of this — the flat vertex lighting, no z-buffer gradient
    /// in the fog, 300-triangle cars. Those stay at every level. This only
    /// decides how coarse the OUTPUT is, and the default is now the sharp end.
    ///
    /// Affine texture mapping and vertex snapping USED to be on that list. Both
    /// are off now, on the owner's call, and for the same reason: each one is
    /// only correct on small triangles and wrecks large ones. See _Affine in
    /// PSXLit.shader and PSXGlobals.vertexSnap.
    /// </summary>
    public enum PSXPixels { Sharp = 0, Classic = 1, Retro = 2 }

    /// <summary>
    /// FILM GRADE: the faded-print look over the whole picture — no true black,
    /// no true white, colour drained except the warm ones, light that bleeds.
    ///
    /// Owner, 2026-09-19, over four frames of a car film: "a nostalgic color
    /// grade I would like added as a filter... the old, nostalgic, 90s
    /// feeling. Like playing this game is a dream." The look itself is
    /// PSX/Blit's (see the header there for what was measured off those
    /// frames); this is only the switch. Ships ON — it is the picture the
    /// owner asked for — and the OPTIONS / pause row is how to say no.
    /// </summary>
    public static class FilmGradePrefs
    {
        const string PrefKey = "psx.filmGrade";

        static int cached = -1;

        public static bool Enabled
        {
            get
            {
                if (cached < 0) cached = PlayerPrefs.GetInt(PrefKey, 1);
                return cached != 0;
            }
            set
            {
                int v = value ? 1 : 0;
                if (cached == v) return;
                cached = v;
                PlayerPrefs.SetInt(PrefKey, v);
                PlayerPrefs.Save();
                Changed++;
            }
        }

        /// <summary>Bumped on every change, the way <see cref="PSXQuality.Changed"/>
        /// is, so the display material follows a switch thrown in the pause
        /// menu on the next frame.</summary>
        public static int Changed { get; private set; }

        public static void Toggle() => Enabled = !Enabled;

        public static string Label => Enabled ? "ON" : "OFF";

        /// <summary>What PSX/Blit's _Grade is set to.</summary>
        public static float Amount => Enabled ? 1f : 0f;
    }

    /// <summary>
    /// LENS FX: what lands on the camera's glass — rain drops that refract
    /// the street behind them (a drop in front of a lamp glows: that is the
    /// bokeh) while it rains, and the faint dust discs a bright light shows up
    /// on the lens at night.
    ///
    /// Owner, 2026-09-21, on Need for Speed (2015): "I like the particle
    /// effects on screen for rain and light." The effect itself is PSX/Lens,
    /// a URP pass of its own in SpeedBlurFeature that runs BEFORE the race HUD
    /// (the HUD sits inside the framebuffer PSX/Blit reads, so a drop there
    /// would refract the lap counter); <see cref="LensFx"/> is its per-frame
    /// state. This is only the switch, built the way
    /// <see cref="FilmGradePrefs"/> is. Ships ON — it is the picture the owner
    /// pointed at — and the OPTIONS / pause row is how to say no.
    ///
    /// ONE TRAP it shares with the speed blur: the HUD is only split onto
    /// its own camera, after the lens, while this or SPEED BLUR is on, so
    /// SpeedBlur reads <see cref="Enabled"/> every frame rather than caching
    /// it — turning this off mid-race puts the HUD back in the world camera
    /// on the next frame, which is safe only because the lens is off with it.
    /// </summary>
    public static class SunShadowPrefs
    {
        // SUN SHADOWS: the day pass's two shadow maps (SunShadows). Ships ON -
        // it is the daylight the owner pointed at - and this is how to say no:
        // the maps are a second pass over the scene every frame, and on the
        // slowest phone that may be the frame rate. Built the way
        // FilmGradePrefs is. OFF keeps the rest of the day pass (the
        // shouldered sunlight, the sun in the haze): those cost nothing.
        const string PrefKey = "psx.sunShadows";

        static int cached = -1;

        public static bool Enabled
        {
            get
            {
                if (cached < 0) cached = PlayerPrefs.GetInt(PrefKey, 1);
                return cached != 0;
            }
            set
            {
                int v = value ? 1 : 0;
                if (cached == v) return;
                cached = v;
                PlayerPrefs.SetInt(PrefKey, v);
                PlayerPrefs.Save();
                Changed++;
            }
        }

        public static int Changed { get; private set; }

        public static void Toggle() => Enabled = !Enabled;

        public static string Label => Enabled ? "ON" : "OFF";

        /// <summary>1 when the maps are allowed, 0 when not: PSXGlobals
        /// multiplies both strengths by it.</summary>
        public static float Amount => Enabled ? 1f : 0f;
    }

    public static class LensFxPrefs
    {
        const string PrefKey = "psx.lensFx";

        static int cached = -1;

        public static bool Enabled
        {
            get
            {
                if (cached < 0) cached = PlayerPrefs.GetInt(PrefKey, 1);
                return cached != 0;
            }
            set
            {
                int v = value ? 1 : 0;
                if (cached == v) return;
                cached = v;
                PlayerPrefs.SetInt(PrefKey, v);
                PlayerPrefs.Save();
                Changed++;
            }
        }

        /// <summary>Bumped on every change, the same counter FilmGradePrefs
        /// and PSXQuality keep, for anything that builds from the pref
        /// rather than polling it (and for the self-test's round trip).</summary>
        public static int Changed { get; private set; }

        public static void Toggle() => Enabled = !Enabled;

        public static string Label => Enabled ? "ON" : "OFF";

        /// <summary>1 when the lens is allowed, 0 when not: the factor the
        /// lens state multiplies its rain and dirt by.</summary>
        public static float Amount => Enabled ? 1f : 0f;
    }

    public static class PSXQuality
    {
        public static readonly string[] Names = { "SHARP", "CLASSIC", "RETRO" };
        const string PrefKey = "psx.pixels";
        const int Count = 3;

        static PSXPixels current = (PSXPixels)(-1);

        public static PSXPixels Current
        {
            get
            {
                if ((int)current < 0)
                    current = (PSXPixels)Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, 0), 0, Count - 1);
                return current;
            }
            set
            {
                var v = (PSXPixels)(((int)value % Count + Count) % Count);
                if (v == current) return;
                current = v;
                PlayerPrefs.SetInt(PrefKey, (int)v);
                PlayerPrefs.Save();
                Changed++;
            }
        }

        /// <summary>Bumped on every change, so PSXCameraOutput can rebuild its
        /// framebuffer without an event a scene load could leave dangling.
        /// Same pattern as <see cref="ClusterBulbs"/>.</summary>
        public static int Changed { get; private set; }

        public static void Cycle(int step = 1) => Current = (PSXPixels)((int)Current + step);

        public static string Name => Names[(int)Current];

        /// <summary>
        /// Vertical resolution. The width follows the display — see
        /// PSXCameraOutput, which is a fixed-lines/variable-width machine the
        /// same way a PlayStation was.
        ///
        /// 480 rather than 240 at the top end: double the lines is half the
        /// pixel size, which is the single biggest thing between "console" and
        /// "spreadsheet". It costs four times the fill of the old default and
        /// this scene is a few thousand triangles of untextured-lighting, so
        /// there is nothing to spend it on anyway.
        /// </summary>
        public static int Height => Current == PSXPixels.Sharp ? 480
                                  : Current == PSXPixels.Classic ? 360 : 240;

        /// <summary>Bits per channel before dithering. Five is the real 15-bit
        /// PS1 framebuffer; six halves the banding the dither has to hide, and
        /// therefore halves the dither.</summary>
        public static float ColorDepth => Current == PSXPixels.Retro ? 5f : 6f;

        /// <summary>How much of the Bayer pattern to mix in. At full strength on
        /// a 6-bit buffer the pattern is more visible than the banding it
        /// exists to break up.</summary>
        public static float Dither => Current == PSXPixels.Sharp ? 0.35f
                                    : Current == PSXPixels.Classic ? 0.6f : 1f;
    }
}
