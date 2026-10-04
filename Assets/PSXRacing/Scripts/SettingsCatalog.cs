using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// STEERING SENSITIVITY (owner, 2026-10-04: "There needs to be a steering
    /// sensitivity adjustment slider").
    ///
    /// A multiplier on the player's steer COMMAND - the stick, the keys and
    /// the touch wheel alike - applied in <see cref="PlayerCarInput"/> as the
    /// last thing before the command reaches the car, and clamped to full lock.
    /// The car is not touched: its lock, its speed-sensitive lock, its actuator
    /// rate and the drift layer are the same at every setting, so 100% is
    /// exactly the feel the car had before this existed. Below 100% a full
    /// push asks for less lock (a calmer car at speed, at the price of a wider
    /// hairpin); above it, less push reaches full lock (quicker hands on a
    /// short stick or a small phone wheel).
    ///
    /// The AI never reads this: it writes the car's steerInput directly.
    /// </summary>
    public static class SteerPrefs
    {
        const string PrefKey = "psx.steerSens";
        public const int MinPercent = 50, MaxPercent = 150, DefaultPercent = 100, StepPercent = 5;

        static int cached = -1;

        public static int Percent
        {
            get
            {
                if (cached < 0) cached = Clamp(PlayerPrefs.GetInt(PrefKey, DefaultPercent));
                return cached;
            }
            set
            {
                int v = Clamp(value);
                if (cached == v) return;
                cached = v;
                PlayerPrefs.SetInt(PrefKey, v);
                // Flushed: on Web, PlayerPrefs live in IndexedDB and a closed
                // tab is not a clean quit (the same reason ChaseCamera flushes).
                PlayerPrefs.Save();
            }
        }

        static int Clamp(int v)
        {
            v = Mathf.Clamp(v, MinPercent, MaxPercent);
            return Mathf.RoundToInt(v / (float)StepPercent) * StepPercent;
        }

        public static float Multiplier => Percent / 100f;

        /// <summary>The car's steer command for a control at <paramref name="axis"/>
        /// (-1..1): scaled by the setting and clamped to full lock.</summary>
        public static float Apply(float axis) => Mathf.Clamp(axis * Multiplier, -1f, 1f);

        public static string Label => Percent + "%";

        /// <summary>Drop the cached value so the next read comes from
        /// PlayerPrefs - what a fresh page load does. For the play check's
        /// persistence test.</summary>
        public static void ForgetCache() => cached = -1;
    }

    /// <summary>
    /// The three volumes the game's sound can honestly be split into
    /// (2026-10-04). There is no music or radio: every sound is the car or the
    /// world it touches, and it falls into two groups -
    ///   ENGINE  : the engine voices of every car (EngineAudio), with the
    ///             turbo, the blow-off and the blower (TurboAudio), and the
    ///             start-up and shut-down samples;
    ///   EFFECTS : tyres (TireAudio), wind (WindAudio), impacts and scrapes
    ///             (CollisionAudio), bridge joints (BridgeJoints), and the
    ///             doors and nozzle of the forecourt (ForecourtMode).
    /// MASTER rides on AudioListener.volume through <see cref="AudioFader"/>,
    /// the one hand on that global, so a zone-line fade and the player's level
    /// multiply rather than fight. Each is 0..100% in 5% steps, default 100%.
    /// </summary>
    public static class AudioPrefs
    {
        public enum Channel { Master, Engine, Effects }

        static readonly string[] Keys = { "psx.volMaster", "psx.volEngine", "psx.volEffects" };
        static readonly int[] cached = { -1, -1, -1 };
        static readonly float[] gain = { 1f, 1f, 1f };
        public const int StepPercent = 5;

        public static int Percent(Channel c)
        {
            int i = (int)c;
            if (cached[i] < 0)
            {
                cached[i] = Mathf.Clamp(PlayerPrefs.GetInt(Keys[i], 100), 0, 100);
                gain[i] = cached[i] / 100f;
            }
            return cached[i];
        }

        public static void SetPercent(Channel c, int v)
        {
            int i = (int)c;
            v = Mathf.Clamp(Mathf.RoundToInt(v / (float)StepPercent) * StepPercent, 0, 100);
            if (Percent(c) == v) return;
            cached[i] = v;
            gain[i] = v / 100f;
            PlayerPrefs.SetInt(Keys[i], v);
            PlayerPrefs.Save();
            if (c == Channel.Master) AudioFader.ApplyMaster();
        }

        static float Gain(Channel c) { Percent(c); return gain[(int)c]; }

        /// <summary>Read every frame by the sound components; cached.</summary>
        public static float Master => Gain(Channel.Master);
        public static float Engine => Gain(Channel.Engine);
        public static float Effects => Gain(Channel.Effects);

        public static string Label(Channel c) => Percent(c) + "%";

        public static void ForgetCache()
        {
            for (int i = 0; i < cached.Length; i++) cached[i] = -1;
        }
    }

    /// <summary>The four pages of the settings menu, in strip order.</summary>
    public enum SettingsTab { Gameplay, Visuals, Audio, System }

    /// <summary>
    /// One row of the settings menu. Either a STEP row (<see cref="value"/>
    /// says what it is now, <see cref="apply"/> moves it on - a cycle, a
    /// toggle, or an action that opens a page) or a SLIDER (<see cref="get"/>
    /// and <see cref="set"/> in whole steps from <see cref="min"/> to
    /// <see cref="max"/>, <see cref="show"/> captioning a step).
    /// </summary>
    public sealed class SettingItem
    {
        public string name;
        public string blurb;
        public SettingsTab tab;
        public System.Func<string> value;
        public System.Action apply;
        public int min, max;
        public System.Func<int> get;
        public System.Action<int> set;
        public System.Func<int, string> show;
        public bool IsSlider => get != null;
    }

    /// <summary>
    /// What a drive adds to the menu: the camera, the physics readout and the
    /// debug bench only exist with a car under the player. Null (the front
    /// end's OPTIONS page) leaves those rows out.
    /// </summary>
    public sealed class SettingsHost
    {
        public System.Func<bool> debugInfo;
        public System.Action toggleDebugInfo;
        /// <summary>Null where there is no bench (not a debug career).</summary>
        public System.Action openBench;
    }

    /// <summary>
    /// THE settings, as one list (2026-10-04, the owner: "The game needs a
    /// proper menu with tabs for Gameplay, Visuals, Audio, Settings"). The
    /// pause menu and both front ends' OPTIONS pages open the same
    /// <see cref="SettingsPanel"/> over this list, so a setting is added once
    /// and appears everywhere; every row drives a PlayerPrefs-backed static.
    /// </summary>
    public static class SettingsCatalog
    {
        public static readonly string[] TabNames = { "GAMEPLAY", "VISUALS", "AUDIO", "SETTINGS" };

        /// <summary>The sentence the help line shows while a TAB is under the cursor.</summary>
        public static readonly string[] TabBlurbs =
        {
            "The camera, the steering and the instruments.",
            "How the picture looks: resolution, sky, lens and grade.",
            "How loud the game is: everything, the engines, and the rest.",
            "The screen, the frame rate, the debug tools and the credits.",
        };

        public static List<SettingItem> Items(SettingsHost host, SettingsTab tab,
                                              System.Action openCredits = null)
        {
            var all = Items(host, openCredits);
            all.RemoveAll(i => i.tab != tab);
            return all;
        }

        public static List<SettingItem> Items(SettingsHost host, System.Action openCredits = null)
        {
            var list = new List<SettingItem>();
            void Step(SettingsTab t, string name, System.Func<string> value, string blurb, System.Action apply) =>
                list.Add(new SettingItem { tab = t, name = name, value = value, blurb = blurb, apply = apply });
            void Slide(SettingsTab t, string name, int min, int max, System.Func<int> get,
                       System.Action<int> set, System.Func<int, string> show, string blurb) =>
                list.Add(new SettingItem { tab = t, name = name, min = min, max = max, get = get, set = set,
                                           show = show, blurb = blurb });

            // ---- GAMEPLAY ---------------------------------------------------
            // The camera is a drive's: ChaseCamera reads the saved view on its
            // own Start, and the front end has no camera to show it on.
            if (host != null)
                Step(SettingsTab.Gameplay, "CAMERA",
                     () => ChaseCamera.ViewNames[(int)ChaseCamera.Current],
                     "Which view you drive from. C, Y or Triangle changes it on the road as well.",
                     () => ChaseCamera.CycleView(1));
            // In whole steps of 5%, so a pad or the arrow keys walk it a notch
            // at a time and the 100% notch is always on it.
            Slide(SettingsTab.Gameplay, "STEERING",
                  SteerPrefs.MinPercent / SteerPrefs.StepPercent, SteerPrefs.MaxPercent / SteerPrefs.StepPercent,
                  () => SteerPrefs.Percent / SteerPrefs.StepPercent,
                  v => SteerPrefs.Percent = v * SteerPrefs.StepPercent,
                  v => (v * SteerPrefs.StepPercent) + "%",
                  "How much lock a push of the stick, the keys or the touch wheel asks for. 100% is the standard feel.");
            Step(SettingsTab.Gameplay, "SPEED", () => SpeedUnits.Label,
                 "What the speedometer counts in. MPH by default - it is 1999 in North Carolina.",
                 () => SpeedUnits.Toggle());
            Step(SettingsTab.Gameplay, "CLUSTER BULB", () => ClusterBulbs.Name,
                 "The colour behind the dials after dark.",
                 () => ClusterBulbs.Cycle(1));
            // On foot only - and the CITY edition has no feet: nobody gets out
            // of a car there, so the row would be a switch for nothing.
            if (Edition.HasCareer)
                Step(SettingsTab.Gameplay, "LOOK Y", () => LookPrefs.Label,
                     "Which way the view pitches on foot. NORMAL unless you fly.",
                     () => LookPrefs.Toggle());

            // ---- VISUALS ----------------------------------------------------
            Step(SettingsTab.Visuals, "PICTURE", () => PSXQuality.Name,
                 "How coarse the picture is. SHARP is 480 lines; RETRO is a PlayStation.",
                 () => PSXQuality.Cycle(1));
            Step(SettingsTab.Visuals, "SKY", () => SkyModePrefs.Label,
                 "PHOTO: the photographed skies. DYNAMIC: a computed sky - real sunsets, drifting clouds.",
                 () => SkyModePrefs.Toggle());
            Step(SettingsTab.Visuals, "LENS FX", () => LensFxPrefs.Label,
                 "Rain drops and light bokeh on the lens. Off for a clean lens.",
                 () => LensFxPrefs.Toggle());
            Step(SettingsTab.Visuals, "FILM GRADE", () => FilmGradePrefs.Label,
                 "A faded 90s print: soft blacks, cream whites, light that bleeds. Off for the plain picture.",
                 () => FilmGradePrefs.Toggle());
            Step(SettingsTab.Visuals, "SPEED BLUR", () => SpeedBlurPrefs.Label,
                 "The picture smears into a tunnel as the car gets fast. Off if you would rather it did not.",
                 () => SpeedBlurPrefs.Toggle());
            Step(SettingsTab.Visuals, "SUN SHADOWS", () => SunShadowPrefs.Label,
                 "Daylight casts shadows; tunnels go dark. Off if the game runs slowly by day.",
                 () => SunShadowPrefs.Toggle());

            // ---- AUDIO ------------------------------------------------------
            // Only what the sound really splits into - see AudioPrefs. No
            // music slider: the game has no music.
            int top = 100 / AudioPrefs.StepPercent;
            void Volume(AudioPrefs.Channel c, string name, string blurb) =>
                Slide(SettingsTab.Audio, name, 0, top,
                      () => AudioPrefs.Percent(c) / AudioPrefs.StepPercent,
                      v => AudioPrefs.SetPercent(c, v * AudioPrefs.StepPercent),
                      v => (v * AudioPrefs.StepPercent) + "%", blurb);
            Volume(AudioPrefs.Channel.Master, "MASTER", "Everything the game plays.");
            Volume(AudioPrefs.Channel.Engine, "ENGINE", "Every engine on the road, with its turbo or blower.");
            Volume(AudioPrefs.Channel.Effects, "EFFECTS", "Tyres, wind, impacts, bridge joints, doors and the fuel nozzle.");

            // ---- SETTINGS ---------------------------------------------------
            // FIRST: the one row the browser can undo behind the player's back
            // (a tab switch, the notification shade, Back, Escape) and may not
            // restore by itself. Left out where the platform cannot do it at
            // all - a row guaranteed to do nothing is worse than no row.
            if (FullscreenPrefs.Supported)
                Step(SettingsTab.System, "FULLSCREEN", () => FullscreenPrefs.Label,
                     "Fills the screen and puts the browser's bars away. Say it again if they come back.",
                     () => FullscreenPrefs.Toggle());
            Step(SettingsTab.System, "FRAME RATE", () => FrameRatePrefs.Label,
                 "MAX runs as fast as the screen refreshes (up to 120). 60 saves battery.",
                 () => FrameRatePrefs.Toggle());
            Step(SettingsTab.System, "SHOW FPS", () => FpsOverlayPrefs.Label,
                 "Frame rate, frame time and the worst frame, along the bottom edge.",
                 () => FpsOverlayPrefs.Toggle());
            if (host != null && host.debugInfo != null && host.toggleDebugInfo != null)
            {
                var h = host;
                Step(SettingsTab.System, "DEBUG INFO", () => h.debugInfo() ? "ON" : "OFF",
                     "The live physics readout, top left, while you drive.",
                     () => h.toggleDebugInfo());
            }
            if (host != null && host.openBench != null)
            {
                var open = host.openBench;
                Step(SettingsTab.System, "DEBUG BENCH", () => "OPEN",
                     "Faults, parts, the hour and the weather, and another car - onto this drive.",
                     open);
            }
            if (openCredits != null)
                Step(SettingsTab.System, "CREDITS", () => "OPEN",
                     "The map, terrain and model credits, in full.",
                     openCredits);
            return list;
        }
    }
}
