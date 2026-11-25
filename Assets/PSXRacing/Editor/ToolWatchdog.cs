using System.IO;
using System.Threading;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// A HARD TIMEOUT for the editor tools (night4 4b, after a pop-meter run sat blocked for a day in
    /// PSXPerf). A plain thread, not a coroutine: it fires even when the main thread is stuck. At the
    /// deadline it writes "TIMEOUT &lt;tool&gt; after N min" to &lt;project&gt;/PSXRacing_tool_timeout.txt and to the
    /// tool's own report if named, then ends the process with exit code 3 (the tool's own process only).
    /// PSX_TOOL_MAXMIN overrides every tool's minutes.
    /// </summary>
    public static class ToolWatchdog
    {
        static Thread thread;

        public static void Arm(string tool, float minutes, string reportPath = null)
        {
            if (float.TryParse(System.Environment.GetEnvironmentVariable("PSX_TOOL_MAXMIN") ?? "", out float m) && m > 0f) minutes = m;
            if (thread != null) return;
            string project = Path.GetDirectoryName(Application.dataPath);
            int ms = Mathf.Max(60000, Mathf.RoundToInt(minutes * 60000f));
            thread = new Thread(() =>
            {
                Thread.Sleep(ms);
                string line = "TIMEOUT " + tool + " after " + minutes.ToString("0") + " min (the main thread did not finish; ToolWatchdog)\n";
                try { File.AppendAllText(Path.Combine(project, "PSXRacing_tool_timeout.txt"), System.DateTime.Now.ToString("s") + " " + line); } catch { }
                if (!string.IsNullOrEmpty(reportPath)) { try { File.AppendAllText(reportPath, line); } catch { } }
                System.Environment.Exit(3);
            }) { IsBackground = true, Name = "PSX ToolWatchdog" };
            thread.Start();
        }
    }
}
