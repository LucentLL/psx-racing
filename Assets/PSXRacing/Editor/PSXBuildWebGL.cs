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
        /// <summary>Every scene the player can reach, in build-index order:
        /// LifeHome at 0 then one per circuit, matching TrackCatalog.SceneIndex.
        /// Built from the catalog rather than listed, so adding a track is one
        /// edit and not three.</summary>
        /// <summary>
        /// The shipped scene list — deferred to PSXRacingBuilder so there is
        /// exactly one of them.
        ///
        /// This used to build its OWN copy, and the copy was missing the pizza
        /// shop: the editor's build settings had 14 scenes and the PLAYER had
        /// 13, so GO TO WORK called LoadScene(13) on a build whose highest
        /// index was 12 and nothing happened at all. Nothing threw, the
        /// self-test passed (it reads EditorBuildSettings, which was right),
        /// and the only symptom was a button that did nothing.
        ///
        /// Same shape of bug as CityPreview's private material table earlier the
        /// same day. Two lists that must agree will not.
        /// </summary>
        static string[] ScenePaths(EditionKind edition) => PSXRacingBuilder.SceneOrder(edition);

        /// <summary>The editor menu builds ALL — the whole game, parking
        /// nothing. The editions are built by the tools (-psxEdition).</summary>
        [MenuItem("PSX Racing/Build WebGL")]
        public static void BuildMenu() => Run(DefaultOutput(), EditionKind.All);

        static string DefaultOutput() =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Build", "WebGL");

        public static void BuildFromCommandLine()
        {
            string outDir = DefaultOutput();
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-psxOutput") outDir = args[i + 1];

            // THE EDITION: -psxEdition MAIN|CITY|ALL (tools\build-and-publish.ps1
            // passes it; a root publish is MAIN, a -PagesDir city publish CITY).
            int code = Run(outDir, EditionTarget.Current) ? 0 : 1;
            EditorApplication.Exit(code);
        }

        static bool Run(string outDir, EditionKind edition)
        {
            // A previous edition build killed mid-flight left Resources parked.
            EditionParking.RecoverIfNeeded();
            string ed = Edition.Name(edition);
            try
            {
                // The scene builder normally produces every scene, but a fresh
                // sandbox copy may not have run it yet — and a MISSING scene is
                // a venue whose door loads nothing.
                var scenePaths = ScenePaths(edition);
                foreach (var p in scenePaths)
                {
                    if (File.Exists(p)) continue;
                    Debug.LogError("[PSXBuildWebGL] Scene missing (" + p +
                                   "), running scene builder first.");
                    PSXRacingBuilder.Build();
                    scenePaths = ScenePaths(edition);
                    break;
                }
                Debug.Log("[PSXBuildWebGL] Edition " + ed + ": " + scenePaths.Length + " scenes - " +
                          string.Join(", ", scenePaths.Select(Path.GetFileNameWithoutExtension)));
                if (!File.Exists(LifeHomeSceneBuilder.ScenePath))
                    LifeHomeSceneBuilder.Build();

                // EVERY SHADER THE RUNTIME NAMES SHIPS IN EVERY EDITION: each
                // must be in GraphicsSettings' Always Included Shaders, checked
                // here in a second rather than found missing on the live page
                // (2026-09-29: CITY stripped PSX/Glow and every car lost its
                // lamps). See RuntimeShaders.
                var shaders = RuntimeShaders.Scan();
                var gaps = RuntimeShaders.Gaps(shaders);
                if (gaps.Count > 0)
                {
                    foreach (var g in gaps) Debug.LogError("[PSXBuildWebGL] RUNTIME SHADER MISSING: " + g);
                    return false;
                }
                Debug.Log("[PSXBuildWebGL] Runtime shaders: " + shaders.Needs.Count + " named, every one always included - " +
                          string.Join(", ", shaders.Needs.Select(n => n.Name)));

                PlayerSettings.companyName = "PSX Racing";
                PlayerSettings.productName = "PSX Racing";
                PlayerSettings.runInBackground = true;
                PlayerSettings.defaultWebScreenWidth = 960;
                PlayerSettings.defaultWebScreenHeight = 720;

                // BROTLI, WITH THE JAVASCRIPT FALLBACK.
                //
                // This was Disabled, for a good reason — "a dumb static server
                // (python http.server) can serve it as-is" — and the reason
                // survives, because decompressionFallback is what makes it
                // survive: the loader unpacks the stream itself instead of
                // relying on the server to send Content-Encoding. A dumb static
                // server still works, and so does GitHub Pages, which serves a
                // .br file as opaque bytes and would otherwise hand the browser
                // something it cannot read.
                //
                // What forced it: two mountain roads took WebGL.data to 103.8 MB
                // and GITHUB REFUSES ANY FILE OVER 100 MB. The push was rejected
                // after a successful forty-minute build. Uncompressed was never
                // really free either — Pages was gzipping the whole 48 MB on
                // every single request before this.
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
                PlayerSettings.WebGL.decompressionFallback = true;
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

                // What the player carries: 16-bit textures, prop models without
                // tangents, no URP post data, no splash, LTO wasm.
                ReleaseBudget.Apply();

                Directory.CreateDirectory(outDir);

                var options = new BuildPlayerOptions
                {
                    // LifeHome first: it is scene index 0, the boot scene, in
                    // every edition. Nothing else depends on the order any
                    // more: the runtime finds each scene by path.
                    scenes = scenePaths,
                    locationPathName = outDir,
                    target = BuildTarget.WebGL,
                    targetGroup = BuildTargetGroup.WebGL,
                    options = BuildOptions.None,
                };
                // THE SWITCH. The player's scripts compile with the edition's
                // define (Edition.Baked); nothing is written to the project, so
                // nothing needs restoring and nothing leaks into the editor.
                string define = EditionTarget.DefineFor(edition);
                if (define != null) options.extraScriptingDefines = new[] { define };

                // Resources ship whole, so the other edition's are moved out
                // of Resources for exactly this one call — see EditionParking.
                Debug.Log("[PSXBuildWebGL] Building " + ed + " to " + outDir);
                BuildReport report;
                List<string> parked = new List<string>();
                try
                {
                    parked = EditionParking.Park(edition);
                    report = BuildPipeline.BuildPlayer(options);
                }
                finally
                {
                    EditionParking.Restore();
                }
                var s = report.summary;
                var shaderLines = RuntimeShaders.ReportLines(shaders, report, out var unpacked);
                WriteReport(report, outDir, edition, scenePaths, parked, shaderLines);
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
                // The player exists, but not as a success: no build_ok.txt, so
                // build-and-publish refuses it.
                if (unpacked.Count > 0)
                {
                    foreach (var u in unpacked) Debug.LogError("[PSXBuildWebGL] RUNTIME SHADER MISSING: " + u);
                    return false;
                }

                PickLicenses(outDir, edition);
                SplashLine(outDir, edition);
                File.WriteAllText(Path.Combine(outDir, "psx-edition.txt"), ed + "\n");
                File.WriteAllText(Path.Combine(outDir, "build_ok.txt"),
                    $"WebGL build succeeded {s.totalSize / (1024 * 1024)} MB edition {ed}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[PSXBuildWebGL] FAILED: " + e);
                return false;
            }
        }

        /// <summary>
        /// WHAT THE BUILD WAS ASKED TO CARRY, written down: the edition, its
        /// scenes, what was parked, the shaders the runtime names
        /// (RuntimeShaders), and every source asset the build report
        /// says was packed, largest first (none after an incremental reuse).
        /// The proof of what
        /// SHIPPED ("MAIN has no charlotte_*", "CITY has no stage scene") is
        /// tools\webgl-contents.mjs, which reads WebGL.data itself and checks
        /// it against the scene and parked lines here — asserting the request
        /// says nothing about what shipped (the pizzeria lesson).
        /// Written to the project root as PSXRacing_webgl_report_EDITION.txt
        /// and beside the build as psx-build-report.txt (not published: the
        /// deploy copies index.html, Build/, StreamingAssets/, LICENSES.txt).
        /// </summary>
        static void WriteReport(BuildReport report, string outDir, EditionKind edition,
                                string[] scenes, List<string> parked, List<string> shaderLines)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                var sum = report.summary;
                sb.AppendLine("edition " + Edition.Name(edition));
                sb.AppendLine("result " + sum.result + "  total " + (sum.totalSize / (1024.0 * 1024.0)).ToString("0.00") +
                              " MiB  errors " + sum.totalErrors + "  time " + sum.totalTime);
                sb.AppendLine("define " + (EditionTarget.DefineFor(edition) ?? "(none)"));
                sb.AppendLine("scenes " + scenes.Length);
                foreach (var p in scenes) sb.AppendLine("  scene " + p);
                sb.AppendLine("parked " + parked.Count);
                foreach (var p in parked) sb.AppendLine("  parked " + p);
                // "runtime-shader" lines: RuntimeShaders' list, which
                // webgl-contents.mjs compares with its own scan of the source.
                foreach (var l in shaderLines) sb.AppendLine(l);

                var sizes = new Dictionary<string, ulong>(StringComparer.Ordinal);
                foreach (var pa in report.packedAssets)
                    foreach (var info in pa.contents)
                    {
                        string src = string.IsNullOrEmpty(info.sourceAssetPath) ? "(built-in)" : info.sourceAssetPath;
                        sizes.TryGetValue(src, out ulong have);
                        sizes[src] = have + info.packedSize;
                    }
                // Not always there: an incremental build that reuses its packed
                // content reports NONE (2026-09-29: the MAIN rebuild after a
                // killed first attempt said 0; the CITY build 3640). So this
                // list is a size breakdown, never the proof - that is
                // tools\webgl-contents.mjs, which unpacks WebGL.data itself and
                // checks it against the scene and parked lists above.
                sb.AppendLine(sizes.Count > 0 ? "packed sources " + sizes.Count
                    : "packed sources: none reported for this target - run node tools/webgl-contents.mjs <build dir>");
                foreach (var kv in sizes.OrderByDescending(k => k.Value))
                    sb.AppendLine("  packed " + (kv.Value / 1024.0).ToString("0.0").PadLeft(10) + " KiB  " + kv.Key);
                foreach (var f in report.GetFiles())
                    sb.AppendLine("  file " + f.role + "  " + (f.size / 1024.0).ToString("0.0") + " KiB  " + f.path);

                string text = sb.ToString();
                File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                                  "PSXRacing_webgl_report_" + Edition.Name(edition) + ".txt"), text);
                if (Directory.Exists(outDir)) File.WriteAllText(Path.Combine(outDir, "psx-build-report.txt"), text);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PSXBuildWebGL] could not write the build report: " + e.Message);
            }
        }

        /// <summary>
        /// The loading screen's second line. The PSXMobile template prints
        /// SUNSET CITY GP under the title — the circuit the game began as, and
        /// a venue the CITY edition does not carry, so the Charlotte page
        /// would open on the name of a race it cannot run. CITY says
        /// CHARLOTTE instead; MAIN and ALL keep the template's line. (The
        /// -PagesDir deploy adds its own CHARLOTTE TEST tag beside it.)
        /// </summary>
        static void SplashLine(string outDir, EditionKind edition)
        {
            if (edition != EditionKind.City) return;
            string idx = Path.Combine(outDir, "index.html");
            if (!File.Exists(idx)) return;
            string html = File.ReadAllText(idx);
            const string from = "<h2>SUNSET CITY GP</h2>";
            if (!html.Contains(from))
            {
                Debug.LogWarning("[PSXBuildWebGL] the template's splash line changed - CITY keeps it as written");
                return;
            }
            File.WriteAllText(idx, html.Replace(from, "<h2>CHARLOTTE</h2>"));
        }

        /// <summary>
        /// Credits PER EDITION. The WebGL template copies every file beside
        /// its index.html into the build; a template carrying
        /// LICENSES-MAIN.txt / LICENSES-CITY.txt / LICENSES-ALL.txt gets the
        /// one for this edition published as LICENSES.txt and the others
        /// dropped. (The Charlotte branch's credits.mjs writes one
        /// LICENSES.txt today; on the merge it should write one per edition —
        /// MAIN: OpenStreetMap for the stages + SRTM terrain; CITY:
        /// OpenStreetMap Charlotte + its elevation sources. ODbL's attribution
        /// belongs in both.) A template with neither is left as it was.
        /// </summary>
        static void PickLicenses(string outDir, EditionKind edition)
        {
            if (!Directory.Exists(outDir)) return;
            var perEdition = Directory.GetFiles(outDir, "LICENSES-*.txt");
            if (perEdition.Length == 0) return;
            string mine = Path.Combine(outDir, "LICENSES-" + Edition.Name(edition) + ".txt");
            if (File.Exists(mine)) File.Copy(mine, Path.Combine(outDir, "LICENSES.txt"), true);
            foreach (var f in perEdition) File.Delete(f);
        }
    }
}
