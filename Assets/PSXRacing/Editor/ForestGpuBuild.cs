using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE GPU-DRIVEN FOREST IN A BUILD (MeshForest.Gpu.cs, 2026-11-09). It is
    /// PC content, like the mesh forest itself, and two things keep it so:
    ///
    ///   * the cull (Shaders/PSXForestCull.compute) reaches a player only
    ///     through a MeshForest component's reference. The stage builder sets
    ///     it in the PC scene pass (<see cref="Cull"/>); a scene built before
    ///     it existed gets it here as the WINDOWS player packs the scene
    ///     (<see cref="ForestSceneProcessor"/> - the scene file is not
    ///     touched). A web scene has no MeshForest, and no other build is
    ///     given the reference: no web or phone player carries a compute
    ///     shader.
    ///   * PSX/Lit's procedural-instancing variants (PROCEDURAL_INSTANCING_ON,
    ///     what Graphics.RenderMeshIndirect draws with) are dropped from every
    ///     build but the Windows player's (<see cref="ForestVariantStripper"/>),
    ///     as the HD tier's are: PSX/Lit is always included, so without this
    ///     every web build would compile and ship them.
    /// </summary>
    public static class ForestGpuBuild
    {
        public const string CullPath = "Assets/PSXRacing/Shaders/PSXForestCull.compute";
        public static ComputeShader Cull() => AssetDatabase.LoadAssetAtPath<ComputeShader>(CullPath);

        internal static bool Windows(BuildTarget t) => t == BuildTarget.StandaloneWindows64 || t == BuildTarget.StandaloneWindows;
    }

    class ForestSceneProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => 0;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // (report == null: entering play mode in the editor, where MeshForest finds the cull by path itself)
            if (report == null || !ForestGpuBuild.Windows(report.summary.platform)) return;
            ComputeShader cull = null;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var mf in root.GetComponentsInChildren<MeshForest>(true))
                {
                    if (mf.cullShader != null) continue;
                    if (cull == null) cull = ForestGpuBuild.Cull();
                    if (cull == null) { Debug.LogWarning("[ForestGpuBuild] no " + ForestGpuBuild.CullPath + ": " + scene.name + "'s mesh forest stays on the CPU path"); return; }
                    mf.cullShader = cull;
                    Debug.Log("[ForestGpuBuild] " + scene.name + ": mesh forest given the GPU cull for this build");
                }
        }
    }

    class ForestVariantStripper : IPreprocessShaders
    {
        public int callbackOrder => 0;
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (shader == null || shader.name != "PSX/Lit") return;
            if (ForestGpuBuild.Windows(EditorUserBuildSettings.activeBuildTarget)) return;
            var kw = new ShaderKeyword("PROCEDURAL_INSTANCING_ON");
            for (int i = data.Count - 1; i >= 0; i--)
                if (data[i].shaderKeywordSet.IsEnabled(kw)) data.RemoveAt(i);
        }
    }
}
