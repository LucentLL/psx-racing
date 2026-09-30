using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE 16-BIT DECODE IN THE EDITOR (the colour pass, C1b, 2026-09-29).
    ///
    /// The player stamps _MainTexRaw when a scene loads (PSXTexDecode.cs);
    /// edit mode has no scene load, and every screenshot tool, preview and the
    /// Scene view render there. Without this, every edit-mode frame of the
    /// 16-bit set - which is imported as linear data now - would show the raw
    /// codes, the very x9 picture this pass removes from the player.
    ///
    ///   * Before an edit-mode camera renders, every material of the open
    ///     scenes' renderers and every material in memory is stamped
    ///     (PSXTexDecode.StampAll - the renderers first, because the editor
    ///     loads a scene's materials lazily). In batch mode - the tools -
    ///     before EVERY camera: a tool opens a scene and renders in the same
    ///     call, with no editor tick between. Interactively only when
    ///     something changed (a scene opened, the hierarchy changed, play
    ///     mode ended) or two seconds passed, so the Scene view does not pay
    ///     for it on every repaint.
    ///   * NO .mat EVER SAVES A 1 (PSXTexDecodeSaveGuard): the flag is the
    ///     runtime's to set from the texture, and a baked 1 would outlive the
    ///     import that justified it. A stamp keeps an asset clean when it was
    ///     clean, and whatever still reaches a save is written as 0 (then
    ///     stamped again before the next render).
    /// </summary>
    [InitializeOnLoad]
    static class PSXTexDecodeEditor
    {
        static bool stale = true;
        static double last;

        static PSXTexDecodeEditor()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            EditorSceneManager.sceneOpened += (s, m) => stale = true;
            EditorApplication.hierarchyChanged += () => stale = true;
            EditorApplication.projectChanged += () => stale = true;
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredEditMode) stale = true; };
        }

        static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (Application.isPlaying) return;   // the runtime stamps play mode
            double now = EditorApplication.timeSinceStartup;
            if (!Application.isBatchMode && !stale && now - last < 2.0) return;
            stale = false;
            last = now;
            PSXTexDecode.StampAll();
        }

        internal static void MarkStale() => stale = true;
    }

    /// <summary>Every .mat about to be written gets _MainTexRaw/_DeepTexRaw
    /// 0: the flag is set at runtime from the texture, never stored.</summary>
    class PSXTexDecodeSaveGuard : AssetModificationProcessor
    {
        static string[] OnWillSaveAssets(string[] paths)
        {
            bool zeroed = false;
            foreach (var p in paths)
            {
                if (!p.EndsWith(".mat", System.StringComparison.OrdinalIgnoreCase)) continue;
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m == null) continue;
                foreach (int id in new[] { PSXTexDecode.MainTexRawId, PSXTexDecode.DeepTexRawId })
                    if (m.HasProperty(id) && m.GetFloat(id) != 0f) { m.SetFloat(id, 0f); zeroed = true; }
            }
            if (zeroed)
            {
                PSXTexDecodeEditor.MarkStale();
                // In play mode nothing else would stamp them again.
                if (Application.isPlaying) EditorApplication.delayCall += () => PSXTexDecode.StampAll();
            }
            return paths;
        }
    }
}
