using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// A WINDOWS STANDALONE player (city perf round 1, 2026-10-30; owner: "The
    /// goal is a forza/nfs quality open world in a full windows (and android)
    /// release. But I require web playtesting while away from PC"). The same
    /// scenes, edition ALL, the same on-demand bundles (built for this target)
    /// as PSXBuildWebGL - so the scripted free-roam tour (Scripts/Dev/PerfTour.cs,
    /// <c>-perftour</c>) times one game on two platforms and says which hitches
    /// are the web's and which are ours. MONO: this editor has no Windows
    /// IL2CPP module. Never published.
    ///
    ///   Unity.exe -quit -batchmode -nographics -projectPath &lt;sandbox&gt; -buildTarget StandaloneWindows64
    ///             -executeMethod PSXRacing.EditorTools.PSXBuildWin64.BuildFromCommandLine
    ///             [-psxOutput C:\Users\mcgee\PSXPerf\Builds\Win64] -logFile &lt;log&gt;
    /// Writes &lt;output&gt;\PSXRacing.exe and &lt;output&gt;\build_ok.txt.
    /// </summary>
    public static class PSXBuildWin64
    {
        public static void BuildFromCommandLine()
        {
            string outDir = @"C:\Users\mcgee\PSXPerf\Builds\Win64";
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-psxOutput") outDir = args[i + 1];
            EditorApplication.Exit(Run(outDir) ? 0 : 1);
        }

        static bool Run(string outDir)
        {
            EditionParking.RecoverIfNeeded();
            const BuildTarget target = BuildTarget.StandaloneWindows64;
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != target)
                {
                    Debug.Log("[PSXBuildWin64] switching the active target to " + target + " (a reimport)");
                    if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, target))
                    {
                        Debug.LogError("[PSXBuildWin64] could not switch to " + target);
                        return false;
                    }
                }
                var scenePaths = PSXRacingBuilder.SceneOrder(EditionKind.All);
                foreach (var p in scenePaths)
                    if (!File.Exists(p)) { Debug.LogError("[PSXBuildWin64] scene missing: " + p + " (run the scene build first)"); return false; }
                var shaders = RuntimeShaders.Scan();
                var gaps = RuntimeShaders.Gaps(shaders);
                if (gaps.Count > 0)
                {
                    foreach (var g in gaps) Debug.LogError("[PSXBuildWin64] RUNTIME SHADER MISSING: " + g);
                    return false;
                }
                PlayerSettings.companyName = "PSX Racing";
                PlayerSettings.productName = "PSX Racing";
                PlayerSettings.runInBackground = true;
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.defaultScreenWidth = 1280;
                PlayerSettings.defaultScreenHeight = 720;
                PlayerSettings.resizableWindow = true;
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);

                // the on-demand bundles, built for THIS target (a WebGL bundle does not load here)
                string err = TextureTierTool.BuildBundle(target, out long texBytes);
                if (err != null) { Debug.LogError("[PSXBuildWin64] TEX BUNDLE FAILED: " + err); return false; }
                err = CockpitBundle.Build(target, out long ckBytes);
                if (err != null) { Debug.LogError("[PSXBuildWin64] COCKPIT BUNDLE FAILED: " + err); return false; }
                err = CharlotteBundle.Build(target, out long chBytes);
                if (err != null) { Debug.LogError("[PSXBuildWin64] CHARLOTTE BUNDLE FAILED: " + err); return false; }
                err = EngineBundleBuilder.Build(target, out long enBytes, out var enLines);
                if (err != null) { Debug.LogError("[PSXBuildWin64] ENGINE BUNDLES FAILED: " + err); return false; }
                Debug.Log($"[PSXBuildWin64] bundles: tex {texBytes / 1048576f:F1} MB, cockpit {ckBytes / 1048576f:F1} MB, charlotte {chBytes / 1048576f:F1} MB, engines {enBytes / 1048576f:F1} MB");

                Directory.CreateDirectory(outDir);
                var options = new BuildPlayerOptions
                {
                    scenes = scenePaths,
                    locationPathName = Path.Combine(outDir, "PSXRacing.exe"),
                    target = target,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                };
                BuildReport report;
                try
                {
                    EditionParking.Park(EditionKind.All);
                    report = BuildPipeline.BuildPlayer(options);
                }
                finally { EditionParking.Restore(); }
                var s = report.summary;
                Debug.Log($"[PSXBuildWin64] Result={s.result} size={s.totalSize / (1024 * 1024)}MB errors={s.totalErrors} time={s.totalTime}");
                if (s.result != BuildResult.Succeeded)
                {
                    foreach (var step in report.steps)
                        foreach (var msg in step.messages)
                            if (msg.type == LogType.Error || msg.type == LogType.Exception)
                                Debug.LogError($"[PSXBuildWin64] {step.name}: {msg.content}");
                    return false;
                }
                File.WriteAllText(Path.Combine(outDir, "build_ok.txt"), $"Win64 (Mono) build succeeded {s.totalSize / (1024 * 1024)} MB edition ALL {DateTime.Now:s}\n");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[PSXBuildWin64] FAILED: " + e);
                return false;
            }
        }
    }
}
