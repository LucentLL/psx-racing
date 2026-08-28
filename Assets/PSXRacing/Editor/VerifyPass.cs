using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// One headless launch, every verification: the city preview (with the
    /// new prop lots), the home + beach-town shots, the garage capture, and
    /// the self-test last — it opens scenes, so nothing scene-sensitive runs
    /// after it. Each stage is fenced so one failure still lets the rest
    /// report; the summary line at the end is what scripts grep for.
    /// </summary>
    public static class VerifyPass
    {
        public static void Run()
        {
            int failed = 0;
            void Stage(string name, System.Action act)
            {
                try { act(); }
                catch (System.Exception e)
                {
                    failed++;
                    Debug.LogError("[VerifyPass] " + name + " THREW: " + e);
                }
            }

            Stage("CityPreview", CityPreview.Run);
            Stage("TownPreview", TownPreview.Run);
            Stage("CaptureGarage", PSXScreenshotTool.CaptureGarageOnly);
            Stage("SelfTest", LifeSimSelfTest.Run);

            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "../PSXRacing_verifypass.txt"),
                failed == 0 ? "VERIFY PASS OK" : "VERIFY PASS FAILED (" + failed + ")");
        }
    }
}
