using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE CAMCORDER (owner, 2026-11-16: "replays could look like they're
    /// recorded on camcorders. The tapes can be saved ... then sell for
    /// in-game money"). The replay's default view became a 1999 Hi8 / VHS-C
    /// camcorder: bystanders at the roadside filming with one in their
    /// hands, now and then a buddy's car behind with one out of the
    /// passenger seat, and the picture and the sound of the tape.
    ///
    /// This class is the switchboard and the one place the parts meet: the
    /// director (<see cref="CamcorderCam"/>) publishes what the lens is doing
    /// this frame, the post pass (<see cref="CamcorderPass"/>, PSX/Camcorder)
    /// draws it, the OSD (<see cref="CamcorderOSD"/>) prints the viewfinder,
    /// the mic (<see cref="CamcorderMic"/>) makes it sound like one.
    ///
    /// SWITCHES (environment, read once):
    ///   PSX_CAMCORDER=0  the replay as it was (TRACKSIDE first, no tape look).
    ///   PSX_CAM_MIC=0    the camcorder picture with the game's own sound.
    /// </summary>
    public static class Camcorder
    {
        static int enabled = -1, mic = -1;

        /// <summary>The camcorder replay is on (the default view).</summary>
        public static bool Enabled
        {
            get
            {
                if (enabled < 0) enabled = System.Environment.GetEnvironmentVariable("PSX_CAMCORDER") == "0" ? 0 : 1;
                return enabled == 1;
            }
        }

        /// <summary>The camcorder's sound (mono, the mic's band, wind, AGC).</summary>
        public static bool MicEnabled
        {
            get
            {
                if (mic < 0) mic = System.Environment.GetEnvironmentVariable("PSX_CAM_MIC") == "0" ? 0 : 1;
                return mic == 1 && Enabled;
            }
        }

        /// <summary>For tools: force the switch either way in this process.</summary>
        public static void ForceEnabled(bool on) => enabled = on ? 1 : 0;

        public const string ShaderName = "PSX/Camcorder";

        // ------------------------------------------------------------------
        //  Published each frame by the director (or the VCR), read by the pass
        // ------------------------------------------------------------------
        /// <summary>The camera the look is drawn on, or null for none.</summary>
        public static Camera Camera;
        /// <summary>Focus hunting: 0 sharp .. 1 lost (a soft blur).</summary>
        public static float Defocus;
        /// <summary>The lens's pan rate across the picture, screen widths per
        /// second (+ right): the smear of a whip-pan at 1/60 s.</summary>
        public static float PanSmear;
        /// <summary>A tracking-noise band rolling through (0 .. 1).</summary>
        public static float Tracking;
        /// <summary>Exposure / white balance start over (a cut: the next
        /// shot opens on the last one's settings and hunts from there).</summary>
        public static bool EyeReset;
        /// <summary>Where the eye starts after a cut: a stop up or down.</summary>
        public static float EyeKick;
        /// <summary>Filmed from a car's passenger seat: the dash and the
        /// A-pillar framed in the picture.</summary>
        public static bool Interior;

        /// <summary>The VCR's transport, drawn by the pass: 0 play (the tape's
        /// own picture), 1 pause (the still field's jitter and its bar),
        /// 2 cue / review (the scan's noise bars).</summary>
        public static int Transport;
        /// <summary>The picture is a TAPE on a VCR at home, not the
        /// viewfinder: the OSD is the deck's, not the camcorder's.</summary>
        public static bool Vcr;
        /// <summary>Scan speed for the bars (signed, x play).</summary>
        public static float ScanRate;

        // ------------------------------------------------------------------
        //  The burned-in date stamp
        // ------------------------------------------------------------------
        /// <summary>The career day the tape was shot on (LifeRules.DateOf),
        /// 0 for none (arcade: the calendar's first day).</summary>
        public static int StampDay;
        /// <summary>The clock when the tape starts, hours (21.7 = 9:42 PM).</summary>
        public static float StampClock = 18f;

        /// <summary>"NOV 16 1999", the camcorder's own order.</summary>
        public static string DateStamp(int day)
        {
            var d = PSXRacing.LifeSim.LifeRules.DateOf(Mathf.Max(1, day));
            string[] m = { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };
            return m[d.Month - 1] + " " + d.Day + " " + d.Year;
        }

        /// <summary>"9:42 PM" for a clock in hours (wraps past midnight).</summary>
        public static string TimeStamp(float hours)
        {
            float h = Mathf.Repeat(hours, 24f);
            int hh = Mathf.FloorToInt(h);
            int mm = Mathf.Clamp(Mathf.FloorToInt((h - hh) * 60f), 0, 59);
            string ap = hh >= 12 ? "PM" : "AM";
            int h12 = hh % 12;
            if (h12 == 0) h12 = 12;
            return h12 + ":" + mm.ToString("00") + " " + ap;
        }

        /// <summary>The tape counter, "0:01:23".</summary>
        public static string Counter(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (s / 3600) + ":" + (s / 60 % 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        /// <summary>Clear every published value (a replay ended).</summary>
        public static void Clear()
        {
            Camera = null;
            Defocus = 0f; PanSmear = 0f; Tracking = 0f;
            EyeReset = false; EyeKick = 0f; Interior = false;
            Transport = 0; ScanRate = 0f;
        }
    }
}
