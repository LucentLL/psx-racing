using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The free-roam perf tour (Scripts/Dev/PerfTour.cs) IN THE EDITOR (city perf
    /// round 1): the quick loop between player builds. Opens the boot scene and
    /// enters play mode; the tour seeds a career, enters free roam through the
    /// front end and drives its routes, writing its files to PSX_PERFOUT, then
    /// exits the editor. The request rides in PSX_PERFTOUR as the page's query
    /// ("routes=uptown,tryon&amp;secs=120&amp;tag=ed_before").
    ///
    ///   $env:PSX_PERFTOUR = "routes=uptown&amp;secs=120&amp;tag=ed"; $env:PSX_PERFOUT = "C:\...\out"
    ///   Unity.exe -batchmode -projectPath &lt;sandbox&gt; -executeMethod PSXRacing.EditorTools.PerfTourEditor.Run -logFile &lt;log&gt;
    /// </summary>
    public static class PerfTourEditor
    {
        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            if (System.Environment.GetEnvironmentVariable("PSX_PERFTOUR") == null)
            {
                Debug.LogError("[PerfTourEditor] set PSX_PERFTOUR (the tour's query) first");
                EditorApplication.Exit(1);
                return;
            }
            string boot = EditorBuildSettings.scenes.Length > 0 ? EditorBuildSettings.scenes[0].path : LifeHomeSceneBuilder.ScenePath;
            if (!File.Exists(boot)) { Debug.LogError("[PerfTourEditor] no boot scene " + boot); EditorApplication.Exit(1); return; }
            EditorSceneManager.OpenScene(boot);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            Debug.Log("[PerfTourEditor] entering play mode on " + boot + " with " + System.Environment.GetEnvironmentVariable("PSX_PERFTOUR"));
            EditorApplication.EnterPlaymode();
        }
    }
}
