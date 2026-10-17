using System.IO;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE TOWER-SCALE STRUCTURE, before and after (facade structure,
    /// 2026-10-16): two of the owner's Street View spots - I-77 west of uptown
    /// looking at the skyline (sv_a3_i77w) and North Tryon looking down at the
    /// Square (sv_a1_tryon) - at noon and dusk, through CityPreview.RunUptownRef
    /// (the game's own hours, the far skyline), once with the structure off
    /// (CityMeshes.FacadeStructureOff: the city as it was) and once on, in ONE
    /// Unity job. Needs Charlotte.unity built in the sandbox (no rebuild: the
    /// tiles are built here).
    /// PNGs: Screenshots\facade_&lt;view&gt;_&lt;hour&gt;_{before,after}.png.
    /// Headless: -executeMethod PSXRacing.EditorTools.FacadeStructureShots.Run
    /// </summary>
    public static class FacadeStructureShots
    {
        // name,aimLat,aimLon,aimAgl,dist,bearing,eyeAgl,fov (CityPreview.ExtraUptownViews)
        const string Views =
            // sv_a3_i77w (35.22700, -80.86208): the eye on I-77, the aim the core
            "i77_skyline,35.22741,-80.84217,110,1812,268.6,6,28;" +
            // sv_a1_tryon (35.22880, -80.84078, heading 229): the eye on N Tryon, 250 m up the street from the aim
            "tryon_street,35.22732,-80.84286,45,250,49,2.5,65";
        const string Names = "i77_skyline,tryon_street";

        public static void Run()
        {
            System.Environment.SetEnvironmentVariable("PSX_UPTOWN_EXTRA", Views);
            System.Environment.SetEnvironmentVariable("PSX_UPTOWN_REF", Names);
            if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("PSX_UPTOWN_HOURS")))
                System.Environment.SetEnvironmentVariable("PSX_UPTOWN_HOURS", "noon,dusk");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            bool keep = CityMeshes.FacadeStructureOff;
            try
            {
                foreach (bool on in new[] { false, true })
                {
                    CityMeshes.FacadeStructureOff = !on;
                    CityPreview.RunUptownRef();
                    foreach (var f in Directory.GetFiles(dir, "uptownref_*.png"))
                    {
                        string n = Path.GetFileNameWithoutExtension(f).Substring("uptownref_".Length);
                        string to = Path.Combine(dir, "facade_" + n + (on ? "_after" : "_before") + ".png");
                        if (File.Exists(to)) File.Delete(to);
                        File.Move(f, to);
                    }
                }
            }
            finally { CityMeshes.FacadeStructureOff = keep; }
            Debug.Log("[FacadeShots] done: Screenshots\\facade_*_{before,after}.png");
        }

        /// <summary>PSX/Lit compiled for the PLAYER's graphics API (GLES3,
        /// the WebGL build), not just the editor's: every pass, vertex and
        /// fragment, plain and with PSX_FACADE. Logs [FacadeCompile] OK or
        /// each failing message. Headless (-buildTarget WebGL):
        /// -executeMethod PSXRacing.EditorTools.FacadeStructureShots.CompileCheck</summary>
        public static void CompileCheck()
        {
            var sh = Shader.Find("PSX/Lit");
            if (sh == null) { Debug.LogError("[FacadeCompile] no PSX/Lit"); return; }
            var data = UnityEditor.ShaderUtil.GetShaderData(sh);
            int bad = 0, done = 0;
            foreach (var kw in new[] { new string[0], new[] { "PSX_FACADE" } })
            for (int s = 0; s < data.SubshaderCount; s++)
            {
                var sub = data.GetSubshader(s);
                for (int p = 0; p < sub.PassCount; p++)
                {
                    var pass = sub.GetPass(p);
                    foreach (var st in new[] { UnityEditor.Rendering.ShaderType.Vertex, UnityEditor.Rendering.ShaderType.Fragment })
                    {
                        if (!pass.HasShaderStage(st)) continue;
                        var r = pass.CompileVariant(st, kw, UnityEditor.Rendering.ShaderCompilerPlatform.GLES3x, UnityEditor.BuildTarget.WebGL);
                        done++;
                        if (!r.Success)
                        {
                            bad++;
                            foreach (var m in r.Messages)
                                Debug.LogError($"[FacadeCompile] FAIL pass {p} {st} [{string.Join(" ", kw)}] line {m.line}: {m.message} {m.messageDetails}");
                        }
                    }
                }
            }
            if (bad == 0) Debug.Log($"[FacadeCompile] OK: {done} GLES3 stages compiled");
            else Debug.LogError($"[FacadeCompile] {bad} of {done} GLES3 stages FAILED");
        }
    }
}
