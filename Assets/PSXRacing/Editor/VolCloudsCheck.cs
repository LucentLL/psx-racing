using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE VOLUMETRIC CLOUDS COMPILE FOR WEBGL 2 (2026-10-25). The editor draws
    /// with DX12, so a shader the browser cannot build (a sampler3D, a loop,
    /// a half-float target) would only fail in the published game. This
    /// compiles every pass of the shaders the clouds touch for GLES 3 - the
    /// WebGL 2 compiler - and logs "[VolClouds] GLES3 ..." lines; DynamicSkyShots
    /// runs it first.
    /// </summary>
    public static class VolCloudsCheck
    {
        static readonly string[] Shaders = { VolumetricClouds.ShaderName, "PSX/Sky", "Hidden/PSX/SkyEquirect", "PSX/Lit", "PSX/CarPaint" };

        /// <summary>Batch entry: the check, then quit with its failure count
        /// as the exit code (PSX/Lit joined the list with the matte slush,
        /// PSX/CarPaint with the clean-coat retune of 2026-10-26).</summary>
        public static void RunBatch() => EditorApplication.Exit(Run() == 0 ? 0 : 1);

        public static int Run()
        {
            int bad = 0;
            foreach (string name in Shaders)
            {
                var sh = Shader.Find(name);
                if (sh == null) { Debug.LogError("[VolClouds] GLES3 " + name + ": NOT FOUND"); bad++; continue; }
                foreach (var m in ShaderUtil.GetShaderMessages(sh))
                    if (m.severity == ShaderCompilerMessageSeverity.Error) { Debug.LogError("[VolClouds] editor " + name + ": " + m.message + " line " + m.line); bad++; }
                var data = ShaderUtil.GetShaderData(sh);
                for (int s = 0; s < data.SubshaderCount; s++)
                {
                    var sub = data.GetSubshader(s);
                    for (int p = 0; p < sub.PassCount; p++)
                    {
                        var pass = sub.GetPass(p);
                        foreach (var st in new[] { ShaderType.Vertex, ShaderType.Fragment })
                        {
                            var info = pass.CompileVariant(st, new string[0], ShaderCompilerPlatform.GLES3x, BuildTarget.WebGL);
                            int bytes = info.ShaderData != null ? info.ShaderData.Length : 0;
                            string msgs = "";
                            foreach (var m in info.Messages)
                                if (m.severity == ShaderCompilerMessageSeverity.Error || msgs.Length < 400)
                                    msgs += " | " + m.severity + ": " + m.message + (m.line > 0 ? " (line " + m.line + ")" : "");
                            if (!info.Success) bad++;
                            Debug.Log($"[VolClouds] GLES3 {name} pass {p} {st}: {(info.Success ? "OK" : "FAILED")} {bytes} bytes{msgs}");
                        }
                    }
                }
            }
            Debug.Log("[VolClouds] GLES3 check: " + (bad == 0 ? "ALL PASSES COMPILE" : bad + " FAILURE(S)"));
            return bad;
        }
    }
}
