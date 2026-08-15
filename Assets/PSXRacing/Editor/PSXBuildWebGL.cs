using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// WebGL player build tuned for phone browsers on a LAN: no compression (so a
    /// plain static file server works with no special Content-Encoding headers),
    /// WASM linker, size-optimized IL2CPP.
    ///
    /// Run headless:
    ///   Unity.exe -quit -batchmode -nographics -projectPath &lt;path&gt;
    ///             -executeMethod PSXRacing.EditorTools.PSXBuildWebGL.BuildFromCommandLine
    ///             -logFile &lt;log&gt;
    /// </summary>
    public static class PSXBuildWebGL
    {
        const string ScenePath = "Assets/PSXRacing/Scenes/CityCircuit.unity";

        [MenuItem("PSX Racing/Build WebGL")]
        public static void BuildMenu() => Run(DefaultOutput());

        static string DefaultOutput() =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Build", "WebGL");

        public static void BuildFromCommandLine()
        {
            string outDir = DefaultOutput();
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-psxOutput") outDir = args[i + 1];

            int code = Run(outDir) ? 0 : 1;
            EditorApplication.Exit(code);
        }

        static bool Run(string outDir)
        {
            try
            {
                // The scene builder normally sets this, but a fresh sandbox copy may
                // not have run it yet.
                if (EditorBuildSettings.scenes == null || EditorBuildSettings.scenes.Length == 0 ||
                    !EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
                {
                    if (!File.Exists(ScenePath))
                    {
                        Debug.LogError("[PSXBuildWebGL] Scene missing, running scene builder first.");
                        PSXRacingBuilder.Build();
                    }
                    EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
                }

                PlayerSettings.companyName = "PSX Racing";
                PlayerSettings.productName = "PSX Racing";
                PlayerSettings.runInBackground = true;
                PlayerSettings.defaultWebScreenWidth = 960;
                PlayerSettings.defaultWebScreenHeight = 720;

                // Uncompressed: a dumb static server (python http.server) can serve it as-is.
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
                PlayerSettings.WebGL.decompressionFallback = false;
                PlayerSettings.WebGL.dataCaching = true;
                PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
                PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
                // Custom mobile-first template: full-viewport canvas, gesture blocking,
                // and a tap-to-start that unlocks fullscreen + audio on phones.
                PlayerSettings.WebGL.template =
                    Directory.Exists("Assets/WebGLTemplates/PSXMobile")
                        ? "PROJECT:PSXMobile" : "APPLICATION:Default";
                PlayerSettings.WebGL.powerPreference = WebGLPowerPreference.HighPerformance;
                PlayerSettings.SetIl2CppCompilerConfiguration(
                    NamedBuildTarget.WebGL, Il2CppCompilerConfiguration.Release);
                // Medium, not High: the Input System resolves some layouts by
                // reflection and High has been known to strip them.
                PlayerSettings.SetManagedStrippingLevel(
                    NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);

                Directory.CreateDirectory(outDir);

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = outDir,
                    target = BuildTarget.WebGL,
                    targetGroup = BuildTargetGroup.WebGL,
                    options = BuildOptions.None,
                };

                Debug.Log("[PSXBuildWebGL] Building to " + outDir);
                BuildReport report = BuildPipeline.BuildPlayer(options);
                var s = report.summary;
                Debug.Log($"[PSXBuildWebGL] Result={s.result} size={s.totalSize / (1024 * 1024)}MB " +
                          $"errors={s.totalErrors} time={s.totalTime}");

                if (s.result != BuildResult.Succeeded)
                {
                    foreach (var step in report.steps)
                        foreach (var msg in step.messages)
                            if (msg.type == LogType.Error || msg.type == LogType.Exception)
                                Debug.LogError($"[PSXBuildWebGL] {step.name}: {msg.content}");
                    return false;
                }

                File.WriteAllText(Path.Combine(outDir, "build_ok.txt"),
                    $"WebGL build succeeded {s.totalSize / (1024 * 1024)} MB");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[PSXBuildWebGL] FAILED: " + e);
                return false;
            }
        }
    }
}
