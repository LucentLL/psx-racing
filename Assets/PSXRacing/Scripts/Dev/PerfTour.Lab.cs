using System.Collections;

namespace PSXRacing
{
    /// <summary>The perf tour's &amp;lab=1 (and &amp;labsnaps=1): the LAB check at the tour's standing point
    /// (LabCheck.cs). Files go where the tour's go, named &lt;tag&gt;_&lt;id&gt;_lab.txt.</summary>
    public partial class PerfTour
    {
        string labArg, labSnapsArg;

        bool LabRequested => !string.IsNullOrEmpty(labArg) && labArg != "0";

        IEnumerator LabCheck(string id) =>
            PSXRacing.LabCheck.Run(id, tag, weatherArg, labSnapsArg == "1", (n, b) => PSXPerfTour_Post(n, b, b.Length), Say);
    }
}
