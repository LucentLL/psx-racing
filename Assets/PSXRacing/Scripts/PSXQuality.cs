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
    ///
    /// HD (2026-10-16, the owner: "game resolution increased to 1080p or
    /// 1440p to further progress towards GT4 visual fidelity") sits above
    /// SHARP: the display's own lines, 1080 on a 1080p screen, up to 1440.
    /// It is 3 rather than 0 because the number is what PlayerPrefs keeps,
    /// and a player who chose SHARP keeps SHARP.
    /// </summary>
    public enum PSXPixels { Sharp = 0, Classic = 1, Retro = 2, HD = 3 }

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
    /// <summary>
    /// THE SKY: the photographed panoramas (PHOTO, the look the game shipped
    /// with) or the computed one after Tidewater (DYNAMIC: PSXAtmosphere.cginc -
    /// scattering from the sun's real position, drifting cloud, sun and moon
    /// discs). The owner asked for it as a SWITCH (2026-09-26) so the two can
    /// be compared on the live build; ships PHOTO until they choose.
    /// </summary>
    public static class SkyModePrefs
    {
        const string PrefKey = "psx.skyDynamic";
        static int cached = -1;

        public static bool Dynamic
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
                Changed++;
                TimeOfDay.RefreshSky();
            }
        }

        public static int Changed { get; private set; }
        public static void Toggle() => Dynamic = !Dynamic;
        public static string Label => Dynamic ? "DYNAMIC" : "PHOTO";
    }

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

    /// <summary>
    /// FRAME RATE: MAX (the screen's own refresh) or 60. The owner, 2026-09-25:
    /// "60fps minimum. Preferably 120fps" on a 120 Hz Galaxy S24+.
    ///
    /// It was a hard 60 in PSXBootstrap, which made 120 impossible and, on
    /// WebGL, paced even the 60 off a TIMER: any targetFrameRate there but -1
    /// replaces the browser's requestAnimationFrame with setTimeout, which is
    /// what makes a steady 60 feel uneven. MAX on the web is -1 - the browser
    /// draws at the display's rate. A native build has no browser to ask, and
    /// -1 there means 30 on a phone, so MAX is the display's refresh rate.
    /// Physics stays a fixed 60 Hz either way; every car is an interpolated
    /// rigidbody, so frames between steps are smooth rather than repeated.
    /// </summary>
    public static class FrameRatePrefs
    {
        const string PrefKey = "psx.frameCap";   // 0 = MAX, 60 = locked 60

        static int cached = -1;

        public static bool Max
        {
            get
            {
                if (cached < 0) cached = PlayerPrefs.GetInt(PrefKey, 0);
                return cached == 0;
            }
            set
            {
                int v = value ? 0 : 60;
                if (cached == v) return;
                cached = v;
                PlayerPrefs.SetInt(PrefKey, v);
                PlayerPrefs.Save();
                Apply();
            }
        }

        public static void Toggle() => Max = !Max;

        public static string Label => Max ? "MAX" : "60";

        /// <summary>What Application.targetFrameRate should be now.</summary>
        public static int Target
        {
            get
            {
                if (!Max) return 60;
                if (Application.platform == RuntimePlatform.WebGLPlayer) return -1;
                int hz = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);
                return hz >= 30 ? hz : 120;
            }
        }

        public static void Apply()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Target;
        }
    }

    /// <summary>SHOW FPS: the frame-rate readout (FpsOverlay). Ships OFF - it is
    /// an instrument for tuning, not part of the picture.</summary>
    public static class FpsOverlayPrefs
    {
        const string PrefKey = "psx.showFps";

        static int cached = -1;

        public static bool Enabled
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

        public static void Toggle() => Enabled = !Enabled;

        public static string Label => Enabled ? "ON" : "OFF";
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
        public static readonly string[] Names = { "SHARP", "CLASSIC", "RETRO", "HD" };
        const string PrefKey = "psx.pixels";
        const int Count = 4;
        /// <summary>The PICTURE row's order, finest first. The saved numbers
        /// are the enum's, so this and not the numbers decides what follows
        /// what.</summary>
        static readonly PSXPixels[] Order = { PSXPixels.HD, PSXPixels.Sharp, PSXPixels.Classic, PSXPixels.Retro };

        static PSXPixels current = (PSXPixels)(-1);

        /// <summary>What a player who never touched the row gets: HD on a
        /// desktop, SHARP on a phone (its GPU fills a 480-line frame at the
        /// screen's rate; a 1080-line one it does not). The editor stays on
        /// SHARP too: the scene builder bakes <see cref="Height"/> into every
        /// scene and the shot tools were all measured through 480 lines.</summary>
        public static PSXPixels Default =>
            Application.isMobilePlatform || Application.isEditor ? PSXPixels.Sharp : PSXPixels.HD;

        public static PSXPixels Current
        {
            get
            {
                if ((int)current < 0)
                    current = (PSXPixels)Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, (int)Default), 0, Count - 1);
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

        public static void Cycle(int step = 1)
        {
            int i = System.Array.IndexOf(Order, Current);
            Current = Order[((i + step) % Order.Length + Order.Length) % Order.Length];
        }

        public static string Name => Names[(int)Current];

        /// <summary>
        /// Vertical resolution. The width follows the display — see
        /// PSXCameraOutput, which is a fixed-lines/variable-width machine the
        /// same way a PlayStation was.
        ///
        /// 480 rather than 240 at SHARP: double the lines is half the pixel
        /// size, which is the single biggest thing between "console" and
        /// "spreadsheet". HD goes the rest of the way (<see cref="HDLines"/>).
        /// </summary>
        public static int Height => Current == PSXPixels.HD ? HDLines(Screen.height)
                                  : Current == PSXPixels.Sharp ? 480
                                  : Current == PSXPixels.Classic ? 360 : 240;

        /// <summary>The most lines HD draws: 1440p. A taller display (a 4K
        /// screen at 100%) is upscaled from here, so the fill stays what a
        /// desktop GPU holds at 60.</summary>
        public const int HDMaxLines = 1440;

        /// <summary>
        /// HD's line count: the display's OWN lines, as many as the canvas has
        /// (the page renders at CSS pixels, so a 1080p screen is 1080 lines
        /// full screen), never more than <see cref="HDMaxLines"/> and never
        /// fewer than SHARP's 480. The display's own count rather than a fixed
        /// 1080 because the framebuffer is shown POINT-filtered: 1080 lines on
        /// a 1125-line window doubles every 24th row, a seam across every edge.
        /// </summary>
        public static int HDLines(int displayLines) => Mathf.Clamp(displayLines, 480, HDMaxLines);

        /// <summary>The line count the HUD is laid out in. The race HUD is
        /// ConstantPixelSize on the PSX camera - its type, the cluster and the
        /// map are all sized in SHARP's 480 lines - so a taller framebuffer
        /// draws it at <see cref="HudScaleFor"/>: the same layout at the same
        /// size on screen, only sharper.</summary>
        public const int HudLines = 480;

        /// <summary>The HUD canvas's scale factor in a framebuffer this many
        /// lines tall: lines/480 over 480, and 1 at 480 and below, where every
        /// preset has always drawn it.</summary>
        public static float HudScaleFor(int lines) => lines > HudLines ? lines / (float)HudLines : 1f;

        /// <summary>Bits per channel before dithering. Five is the real 15-bit
        /// PS1 framebuffer; six halves the banding the dither has to hide, and
        /// therefore halves the dither.</summary>
        public static float ColorDepth => Current == PSXPixels.Retro ? 5f : 6f;

        /// <summary>How much of the Bayer pattern to mix in. At full strength on
        /// a 6-bit buffer the pattern is more visible than the banding it
        /// exists to break up. HD keeps SHARP's: its cells are a screen pixel,
        /// so the same strength is already a finer grain.</summary>
        public static float Dither => Current == PSXPixels.Sharp || Current == PSXPixels.HD ? 0.35f
                                    : Current == PSXPixels.Classic ? 0.6f : 1f;
    }
}
