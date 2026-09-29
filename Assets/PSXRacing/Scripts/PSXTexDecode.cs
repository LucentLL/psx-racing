using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PSXRacing
{
    /// <summary>
    /// THE ONE WRITER OF _MainTexRaw (the colour pass, C1b, 2026-09-29).
    ///
    /// The 16-bit texture set (Editor/ReleaseBudget.cs, asset label psx16) is
    /// imported as LINEAR DATA on every platform, so no GPU decodes it and
    /// the PSX shaders decode the texels themselves (Shaders/PSXTexDecode.cginc)
    /// - but only on a material that says its texture needs it. This class is
    /// what says so, and the only thing that does:
    ///
    ///     _MainTexRaw = 1  exactly when  _MainTex is a Texture2D with !isDataSRGB
    ///
    /// (and _DeepTexRaw for the water's second sheet). The rule reads the
    /// TEXTURE, never the build target: the sRGB import flag is the same on
    /// every platform, so the flag means the same thing in the editor on
    /// either target and in the player. It is never baked into a .mat - a
    /// flag written at build time would freeze whatever import the baking
    /// sandbox happened to hold (the colour plan's critic, point 3) - and the
    /// editor keeps it out of every saved .mat (PSXTexDecodeEditor).
    ///
    /// WHEN it runs: a material has to be stamped before it is first drawn.
    ///   * every scene load: every renderer of the scene, then every material
    ///     in memory (SceneManager.sceneLoaded, which comes after the scene's
    ///     Awakes - SeasonDress has already put the day's dress on - and
    ///     before the first frame);
    ///   * every prefab the game loads from Resources (the car shells, the
    ///     city props, the pizza cargo): LoadPrefab stamps what it wears
    ///     before anything instantiates it, and the self-test fails any
    ///     Resources.Load of a GameObject that does not come through here;
    ///   * the car dressers (CarBody.Apply) and SeasonDress.Apply, for the
    ///     materials they put on at runtime;
    ///   * in the editor, before every edit-mode camera render
    ///     (PSXTexDecodeEditor), so every tool frame is the player's picture.
    /// </summary>
    public static class PSXTexDecode
    {
        public static readonly int MainTexRawId = Shader.PropertyToID("_MainTexRaw");
        public static readonly int DeepTexRawId = Shader.PropertyToID("_DeepTexRaw");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int DeepTexId = Shader.PropertyToID("_DeepTex");

        /// <summary>The rule: a Texture2D whose texels no GPU decodes from
        /// sRGB. Render textures and textures made at runtime are sRGB (or
        /// not colour) and never qualify.</summary>
        public static bool IsRaw(Texture t) => t is Texture2D t2 && !t2.isDataSRGB;

        /// <summary>What the flag must be on <paramref name="m"/> for
        /// <paramref name="flagId"/> (_MainTexRaw or _DeepTexRaw); -1 when the
        /// material's shader has no such flag.</summary>
        public static int Want(Material m, int flagId)
        {
            if (m == null || !m.HasProperty(flagId)) return -1;
            int texId = flagId == DeepTexRawId ? DeepTexId : MainTexId;
            return m.HasProperty(texId) && IsRaw(m.GetTexture(texId)) ? 1 : 0;
        }

        /// <summary>Set the flags on one material from its textures. True
        /// when anything changed.</summary>
        public static bool Stamp(Material m)
        {
            if (m == null) return false;
            bool changed = false;
            changed |= StampOne(m, MainTexRawId);
            changed |= StampOne(m, DeepTexRawId);
            return changed;
        }

        static bool StampOne(Material m, int flagId)
        {
            int want = Want(m, flagId);
            if (want < 0 || m.GetFloat(flagId) == want) return false;
#if UNITY_EDITOR
            // A material ASSET stamped in the editor must not become a saved
            // value: keep it clean unless something else had already dirtied
            // it (and then the save guard in PSXTexDecodeEditor writes 0).
            if (UnityEditor.EditorUtility.IsPersistent(m))
            {
                bool wasDirty = UnityEditor.EditorUtility.IsDirty(m);
                m.SetFloat(flagId, want);
                if (!wasDirty) UnityEditor.EditorUtility.ClearDirty(m);
                return true;
            }
#endif
            m.SetFloat(flagId, want);
            return true;
        }

        /// <summary>Every material in memory: the scene's, its dress
        /// variants, whatever Resources has loaded. Returns how many changed.</summary>
        public static int StampLoaded()
        {
            int n = 0;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (Stamp(m)) n++;
            return n;
        }

        /// <summary>How many materials in memory decode today (for a shot's
        /// sidecar and the audit's log).</summary>
        public static int CountDecoding()
        {
            int n = 0;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && m.HasProperty(MainTexRawId) && m.GetFloat(MainTexRawId) > 0.5f) n++;
            return n;
        }

        /// <summary>Every material on every renderer of every loaded scene,
        /// inactive ones included. FindObjectsOfTypeAll only sees materials
        /// already in memory, and the EDITOR loads a scene's materials lazily
        /// - the first render of a freshly opened scene pulls its road in, so
        /// a stamp of "what is loaded" just before it missed the road
        /// (measured: 43 of 96 materials on the first frame of a run). Asking
        /// the renderers loads what they wear.</summary>
        public static int StampScenes()
        {
            int n = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                n += StampScene(SceneManager.GetSceneAt(i));
            return n;
        }

        /// <summary>Every material on the renderers of one scene.</summary>
        public static int StampScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return 0;
            int n = 0;
            foreach (var root in scene.GetRootGameObjects())
                n += StampTree(root);
            return n;
        }

        /// <summary>Everything: the loaded scenes' renderers (which loads
        /// what they wear), then every material in memory (a dress variant
        /// not worn yet, a prefab's, DontDestroyOnLoad's).</summary>
        public static int StampAll() => StampScenes() + StampLoaded();

        /// <summary>Every material on these renderers.</summary>
        public static int StampRenderers(IList<Renderer> renderers)
        {
            int n = 0;
            if (renderers == null) return 0;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                foreach (var m in r.sharedMaterials)
                    if (Stamp(m)) n++;
            }
            return n;
        }

        /// <summary>Every material under <paramref name="go"/>, inactive
        /// children included - a prefab asset or a live object alike.</summary>
        public static int StampTree(GameObject go)
        {
            if (go == null) return 0;
            return StampRenderers(go.GetComponentsInChildren<Renderer>(true));
        }

        /// <summary>Resources.Load of a prefab, with what it wears stamped
        /// before anything can instantiate it. The only door a GameObject
        /// comes out of Resources by (the self-test holds Scripts to that).</summary>
        public static GameObject LoadPrefab(string path)
        {
            var go = Resources.Load<GameObject>(path);
            if (go != null) StampTree(go);
            return go;
        }

        // ------------------------------------------------------------------
        //  The scene-load pass
        // ------------------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            // Unsubscribe first: with domain reload off (the play checks run
            // that way) a second play session would otherwise stamp twice.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            int n = StampScene(scene) + StampLoaded();
            if (n > 0) Debug.Log("[PSXTexDecode] " + scene.name + ": " + n + " material(s) set to decode their 16-bit texels");
        }
    }
}
