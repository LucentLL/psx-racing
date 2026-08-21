using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// Updates the UGUI HUD: speed, gear, RPM bar, lap, times, position,
    /// countdown and results.
    ///
    /// Every text field is change-gated. Assigning Text.text rebuilds the mesh
    /// and allocates, and most of these values change once a lap — rebuilding
    /// all seven strings every frame was the single largest managed allocation
    /// in the game.
    /// </summary>
    public class RaceHUD : MonoBehaviour
    {
        public CarController car;
        public Text speedText;
        public Text gearText;
        public Text lapText;
        public Text timeText;
        public Text lastLapText;
        public Text posText;
        public Text centerText;
        public Image rpmFill;

        int lastSpeed = int.MinValue;
        int lastGear = int.MinValue;
        int lastLap = int.MinValue;
        int lastPos = int.MinValue;
        int lastTimeCentis = int.MinValue;
        float lastBest = -1f;
        string lastCenter;

        static readonly string[] GearNames = { "R", "N", "1", "2", "3", "4", "5", "6" };

        /// <summary>Invariant culture: on a machine with a comma decimal
        /// separator the lap clock would otherwise read 1'23,456.</summary>
        static string FormatTime(float t)
        {
            if (t <= 0f) return "--'--\"---";
            int m = (int)(t / 60f);
            float s = t - m * 60;
            return string.Format(CultureInfo.InvariantCulture, "{0}'{1:00.000}", m, s)
                         .Replace(".", "\"");
        }

        static void Set(Text field, string value)
        {
            if (field != null && field.text != value) field.text = value;
        }

        void Update()
        {
            var rm = RaceManager.Instance;
            if (car == null || rm == null) return;
            var p = rm.GetProgress(car);

            int speed = Mathf.RoundToInt(car.speedKmh);
            if (speed != lastSpeed) { lastSpeed = speed; Set(speedText, speed + " km/h"); }

            int gear = car.currentGear;
            if (gear != lastGear)
            {
                lastGear = gear;
                int idx = Mathf.Clamp(gear + 1, 0, GearNames.Length - 1);
                Set(gearText, GearNames[idx]);
            }

            if (rpmFill != null)
            {
                float f = Mathf.InverseLerp(0f, car.revLimitRPM, car.currentRPM);
                rpmFill.fillAmount = f;
                rpmFill.color = car.currentRPM > car.redlineRPM
                    ? new Color(1f, 0.25f, 0.2f) : new Color(1f, 0.85f, 0.3f);
            }

            if (p != null)
            {
                int lap = Mathf.Min(p.lap, rm.totalLaps);
                if (lap != lastLap) { lastLap = lap; Set(lapText, "LAP " + lap + "/" + rm.totalLaps); }

                // The clock only needs redrawing when a hundredth ticks over.
                int centis = Mathf.FloorToInt(p.raceTime * 100f);
                if (centis != lastTimeCentis) { lastTimeCentis = centis; Set(timeText, FormatTime(p.raceTime)); }

                if (!Mathf.Approximately(p.bestLapTime, lastBest))
                {
                    lastBest = p.bestLapTime;
                    Set(lastLapText, p.bestLapTime > 0f ? "BEST " + FormatTime(p.bestLapTime) : "");
                }
            }

            int pos = rm.GetPosition(car);
            if (pos != lastPos) { lastPos = pos; Set(posText, "POS " + pos + "/" + rm.allCars.Count); }

            string center = null;
            switch (rm.State)
            {
                case RaceManager.RaceState.Countdown:
                    float remaining = rm.CountdownRemaining - 1f;
                    center = remaining > 0f ? Mathf.CeilToInt(remaining).ToString() : "GO!";
                    break;
                case RaceManager.RaceState.Racing:
                    center = rm.CountdownRemaining > 0f ? "GO!" : "";
                    break;
                case RaceManager.RaceState.Finished:
                    center = "FINISH!  P" + pos +
                             "\nBEST " + FormatTime(p != null ? p.bestLapTime : 0f) +
                             (RaceHandoff.FromLifeSim
                                ? "\n\nPRESS R TO GO HOME"
                                : "\n\nPRESS R TO RESTART");
                    break;
            }
            if (center != lastCenter) { lastCenter = center; Set(centerText, center); }
        }
    }
}
