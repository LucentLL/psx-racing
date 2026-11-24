using System.Text;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// NIGHT 4's own clock (round 4b): the main-thread milliseconds spent each frame inside the headlight
    /// fans (CarLights.OccUpdate) and the street-lamp view (StreetLights.N4UpdateVis / N4Tick), with the
    /// frame's own unscaled delta, appended to the file PSX_N4CLOCK names (a line a frame: frame, dt ms,
    /// occ ms, view ms). Off (nothing recorded, nothing allocated) unless PSX_N4CLOCK is set: a tool.
    /// </summary>
    public static class Night4Clock
    {
        static readonly string path = System.Environment.GetEnvironmentVariable("PSX_N4CLOCK");
        public static bool On => !string.IsNullOrEmpty(path);
        static int frame = -1;
        static float occ, view, tick, dt;
        static StringBuilder buf;
        static bool hooked;

        static void Roll()
        {
            if (Time.frameCount == frame) return;
            if (frame >= 0)
            {
                buf ??= new StringBuilder(1 << 16);
                buf.Append(frame).Append(',').Append((dt * 1000f).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                   .Append(',').Append(occ.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture))
                   .Append(',').Append(view.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                if (buf.Length > 60000) Flush();
            }
            if (!hooked) { hooked = true; Application.quitting += Flush; }
            frame = Time.frameCount; occ = 0f; view = 0f; tick = 0f; dt = Time.unscaledDeltaTime;
        }

        static void Flush()
        {
            if (buf == null || buf.Length == 0) return;
            try { System.IO.File.AppendAllText(path, buf.ToString()); } catch { }
            buf.Length = 0;
        }

        public static void Occ(float ms) { if (!On) return; Roll(); occ += ms; }
        public static void View(float ms) { if (!On) return; Roll(); view += ms; }
        public static void Tick(float ms) { if (!On) return; Roll(); view += ms; tick += ms; }
    }
}
