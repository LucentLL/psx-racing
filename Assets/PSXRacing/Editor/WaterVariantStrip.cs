using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// WATER PART B (item 13): the HD water is a PC picture (WaterPrefs.Offered:
    /// a phone's or a browser's pipeline makes neither copy it reads, and the row
    /// is never offered there). Its variant of PSX/Water is left out of every
    /// build that is not a Windows player, so an APK or a WebGL build never
    /// compiles it - Vulkan's compiler refused it ("top-level params outside of
    /// cbuffers", WaterShots' compile set, 2026-11-17) - and never carries it.
    /// A Windows build keeps both variants.
    /// </summary>
    public static class WaterVariantStrip
    {
        internal static bool Desktop(BuildTarget t) => ForestGpuBuild.Windows(t);
    }

    class WaterVariantStripper : IPreprocessShaders
    {
        public int callbackOrder => 0;
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (shader == null || shader.name != WaterLook.ShaderName) return;
            if (WaterVariantStrip.Desktop(EditorUserBuildSettings.activeBuildTarget)) return;
            var kw = new ShaderKeyword(WaterLook.Keyword);
            int before = data.Count;
            for (int i = data.Count - 1; i >= 0; i--)
                if (data[i].shaderKeywordSet.IsEnabled(kw)) data.RemoveAt(i);
            if (data.Count != before)
                Debug.Log($"[WaterVariantStrip] {EditorUserBuildSettings.activeBuildTarget}: PSX/Water's {WaterLook.Keyword} variant left out ({before - data.Count} of {before})");
        }
    }
}
