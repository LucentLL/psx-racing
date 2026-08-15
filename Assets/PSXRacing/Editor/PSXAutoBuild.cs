using System.IO;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// One-shot auto-build: if a "psx_autobuild.flag" file exists at the project
    /// root when scripts reload, run the scene builder once and delete the flag.
    /// Lets the build be triggered from outside the editor; harmless otherwise.
    /// </summary>
    [InitializeOnLoad]
    public static class PSXAutoBuild
    {
        static string FlagPath =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "psx_autobuild.flag");

        static PSXAutoBuild()
        {
            if (File.Exists(FlagPath))
                EditorApplication.delayCall += TryBuild;
        }

        static void TryBuild()
        {
            if (!File.Exists(FlagPath)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryBuild;
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            File.Delete(FlagPath);
            Debug.Log("[PSXAutoBuild] Flag found - building PSX Racing scene.");
            PSXRacingBuilder.Build();
        }
    }
}
