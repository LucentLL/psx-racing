using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE HD TIER (owner, 2026-11-02: "the new goal for the game is to achieve
    /// a PS3-PS4 era visual fidelity", the PC build; textures from the owner's
    /// packs upscaled 4x in Codex with a PBR set each - AGENTS.md's HD rule).
    ///
    /// A third tier over CLASSIC and ENHANCED (TextureTier), for the PC
    /// profile only: a material whose main texture has an HD set gets the set's
    /// colour in _MainTex, its normal, roughness and AO maps, and PSX/Lit's
    /// PSX_HD keyword - the sun per pixel off the normal map, the AO on the
    /// sky's light, a roughness highlight. Runs inside PSXTexDecode.Stamp after
    /// the Classic/Enhanced swap, on every material the game shows.
    ///
    /// The sets ship as ONE AssetBundle beside the Windows player
    /// (StreamingAssets/<see cref="BundleFile"/>, copied in by PSXBuildWin64,
    /// never into the project - a web build must not carry gigabytes). The
    /// editor reads them from Assets/PSXRacing/HDSrc (gitignored) when
    /// PSX_HD=1. Atlas-cell and facade materials are left alone (their cells
    /// would need HD cells; a later pass). -psx-no-hd turns it off in the
    /// player for an A/B.
    /// </summary>
    public static class TextureHD
    {
        public const string BundleFile = "texhd.bundle";
#if UNITY_EDITOR
        public const string EditorSrcRoot = "Assets/PSXRacing/HDSrc/TexHD/";
#endif
        public const string Keyword = "PSX_HD";
        public const string NormalSuffix = "_normal", RoughSuffix = "_rough", AOSuffix = "_ao";

        struct HDSet { public Texture2D col, nrm, rough, ao; }

        static TextureHDMap map;
        static bool mapTried, bundleTried;
        static AssetBundle bundle;
        static readonly Dictionary<Texture, int> byOriginal = new Dictionary<Texture, int>();
        static readonly Dictionary<Texture, int> byHD = new Dictionary<Texture, int>();
        static HDSet[] sets;
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int NormalId = Shader.PropertyToID("_HDNormal"), RoughId = Shader.PropertyToID("_HDRough"), AOId = Shader.PropertyToID("_HDAO");

        /// <summary>The HD tier is wanted: the PC profile, not refused.</summary>
        public static bool Want
        {
            get
            {
#if UNITY_EDITOR
                return System.Environment.GetEnvironmentVariable("PSX_HD") == "1";
#else
                return PlatformProfile.PC && !NoHDArg;
#endif
            }
        }
        static int noHD = -1;
        static bool NoHDArg
        {
            get
            {
                if (noHD < 0)
                {
                    noHD = 0;
                    foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-psx-no-hd") noHD = 1;
                }
                return noHD == 1;
            }
        }

        /// <summary>Sets in the table, and sets live on materials now (the FPS
        /// overlay's / the log's count).</summary>
        public static int SetCount => map != null && map.pairs != null ? map.pairs.Length : 0;
        public static int Applied { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (!Want) return;
            foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-psx-junction-hd") JunctionOnBase = false;
            if (EnsureMap() && EnsureBundle())
                Debug.Log($"[TextureHD] HD tier on: {SetCount} PBR sets + {RoadCount} road textures on the road base from {BundleFile}" +
                          (JunctionOnBase ? "" : " (junction slabs on their own set)"));
        }

        /// <summary>Forget the table (the editor, after TextureHDTool.Import
        /// rewrote it): the next Apply reads it again.</summary>
        public static void ResetMap()
        {
            map = null; mapTried = false; sets = null;
            byOriginal.Clear(); byHD.Clear(); roadByOriginal.Clear(); baseSets.Clear(); masks.Clear();
        }

        static bool EnsureMap()
        {
            if (map != null) return true;
            if (mapTried) return false;
            mapTried = true;
            map = Resources.Load<TextureHDMap>(TextureHDMap.ResourcePath);
            if (map == null) return false;
            if (map.pairs == null) map.pairs = new TextureHDMap.Pair[0];
            if (map.roads == null) map.roads = new TextureHDMap.Road[0];
            if (map.pairs.Length == 0 && map.roads.Length == 0) { map = null; return false; }
            sets = new HDSet[map.pairs.Length];
            for (int i = 0; i < map.pairs.Length; i++)
                if (map.pairs[i].original != null) byOriginal[map.pairs[i].original] = i;
            for (int i = 0; i < map.roads.Length; i++)
                if (map.roads[i].original != null) roadByOriginal[map.roads[i].original] = i;
            return true;
        }

        static bool EnsureBundle()
        {
#if UNITY_EDITOR
            return true;   // the editor reads HDSrc
#else
            if (bundle != null) return true;
            if (bundleTried) return false;
            bundleTried = true;
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, BundleFile);
            if (!System.IO.File.Exists(path)) { Debug.Log("[TextureHD] no " + BundleFile + " beside the player - HD tier off"); return false; }
            // local disk: the header only now, each texture when first asked for
            bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null) Debug.LogWarning("[TextureHD] " + BundleFile + " did not load - HD tier off");
            return bundle != null;
#endif
        }

        static Texture2D Load(string name)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(EditorSrcRoot + name + ".png");
#else
            return bundle != null ? bundle.LoadAsset<Texture2D>(name) : null;
#endif
        }

        static bool Get(int i, out HDSet s)
        {
            s = sets[i];
            if (s.col != null) return true;
            string key = map.pairs[i].key;
            s.col = Load(key);
            if (s.col == null) return false;
            s.nrm = Load(key + NormalSuffix);
            s.rough = Load(key + RoughSuffix);
            s.ao = Load(key + AOSuffix);
            sets[i] = s;
            byHD[s.col] = i;
            return true;
        }

        /// <summary>The original texture an HD colour map stands for (the
        /// terrain detail and TextureTier ask by the original's name).</summary>
        public static bool OriginalOf(Texture t, out Texture original)
        {
            original = null;
            if (t == null || map == null || !byHD.TryGetValue(t, out int i)) return false;
            original = map.pairs[i].original;
            return original != null;
        }

        /// <summary>Put the HD set on one material, or take it off. True when
        /// anything changed. Called by PSXTexDecode.Stamp after the
        /// Classic/Enhanced swap.</summary>
        public static bool Apply(Material m)
        {
            if (m == null || !m.HasProperty(NormalId)) return false;   // not PSX/Lit
            bool on = m.IsKeywordEnabled(Keyword);
            if (!Want || !EnsureMap() || !EnsureBundle())
            {
                if (!on) return false;
                Off(m);
                return true;
            }
            // atlas cells and facades keep their tier (their cells are not HD yet)
            if (m.IsKeywordEnabled("PSX_ATLAS_RECT") || m.IsKeywordEnabled("PSX_FACADE") || m.IsKeywordEnabled("PSX_FURNITURE")) return false;
            var main = m.GetTexture(MainTexId);
            if (main == null) return false;
            if (on && byHD.ContainsKey(main)) return false;   // already HD
            if (on && m.HasProperty(RoadOnId) && m.GetFloat(RoadOnId) > 0.5f) return false;   // already on the road base
            var orig = TextureTier.OriginalOf(main);
            if (orig != null && roadByOriginal.TryGetValue(orig, out int ri) && (map.roads[ri].paint != null || JunctionOnBase) && ApplyRoad(m, ri))
                return true;
            if (orig == null || !byOriginal.TryGetValue(orig, out int i) || !Get(i, out var s)) return false;
            Set(m, MainTexId, s.col);
            if (s.nrm != null) Set(m, NormalId, s.nrm);
            if (s.rough != null) Set(m, RoughId, s.rough);
            if (s.ao != null) Set(m, AOId, s.ao);
            m.EnableKeyword(Keyword);
            Applied++;
            return true;
        }

        static void Off(Material m)
        {
            var main = m.GetTexture(MainTexId);
            if (main != null && map != null && byHD.TryGetValue(main, out int i) && map.pairs[i].original != null)
                Set(m, MainTexId, TextureTier.ForTier(map.pairs[i].original));
            if (m.HasProperty(RoadOnId) && m.GetFloat(RoadOnId) != 0f) SetF(m, RoadOnId, 0f);
            m.DisableKeyword(Keyword);
            Applied = Mathf.Max(0, Applied - 1);
        }

        /// <summary>Take the HD tier off every material in memory (an A/B in
        /// the editor: then PSXTexDecode.StampAll puts back what Want says).</summary>
        public static int OffAll()
        {
            int n = 0;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && m.HasProperty(NormalId) && m.IsKeywordEnabled(Keyword)) { Off(m); n++; }
            return n;
        }

        // ---- THE ROAD BASE (TextureHDMap.Road; PSX/Lit _RoadBaseOn) ----
        public const string PaintSuffix = "_paint";
        /// <summary>The junction slabs drawn on the 4 m road base (true, so a
        /// fan matches the ribbons it meets) or with their own 12 m HD set
        /// (false; -psx-junction-hd in the player).</summary>
        public static bool JunctionOnBase = true;
        static readonly Dictionary<Texture, int> roadByOriginal = new Dictionary<Texture, int>();
        static readonly Dictionary<string, HDSet> baseSets = new Dictionary<string, HDSet>();
        static readonly Dictionary<string, Texture2D> masks = new Dictionary<string, Texture2D>();
        static readonly int RoadOnId = Shader.PropertyToID("_RoadBaseOn"), RoadTexId = Shader.PropertyToID("_RoadBaseTex"),
                            RoadPaintId = Shader.PropertyToID("_RoadPaint"), RoadRepeatId = Shader.PropertyToID("_RoadBaseRepeat"),
                            PaintWId = Shader.PropertyToID("_RoadPaintW"), PaintYId = Shader.PropertyToID("_RoadPaintY");

        /// <summary>Road base textures in the table (the log's count).</summary>
        public static int RoadCount => map != null && map.roads != null ? map.roads.Length : 0;

        static bool ApplyRoad(Material m, int ri)
        {
            if (!m.HasProperty(RoadOnId)) return false;
            var r = map.roads[ri];
            if (!baseSets.TryGetValue(r.baseKey, out var s) || s.col == null)
            {
                s.col = Load(r.baseKey);
                if (s.col == null) return false;
                s.nrm = Load(r.baseKey + NormalSuffix);
                s.rough = Load(r.baseKey + RoughSuffix);
                s.ao = Load(r.baseKey + AOSuffix);
                baseSets[r.baseKey] = s;
            }
            Texture2D mask = null;
            if (!string.IsNullOrEmpty(r.paint) && !masks.TryGetValue(r.paint, out mask))
                masks[r.paint] = mask = Load(r.paint);
            Set(m, RoadTexId, s.col);
            Set(m, RoadPaintId, mask);
            if (s.nrm != null) Set(m, NormalId, s.nrm);
            if (s.rough != null) Set(m, RoughId, s.rough);
            if (s.ao != null) Set(m, AOId, s.ao);
            SetV(m, RoadRepeatId, new Vector4(r.repeat.x, r.repeat.y, mask != null ? 1f : 0f, 0f));
            SetV(m, PaintWId, map.paintWhite);
            SetV(m, PaintYId, map.paintYellow);
            SetF(m, RoadOnId, 1f);
            m.EnableKeyword(Keyword);
            Applied++;
            return true;
        }

        static void SetF(Material m, int id, float v)
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(m))
            {
                bool wasDirty = UnityEditor.EditorUtility.IsDirty(m);
                m.SetFloat(id, v);
                if (!wasDirty) UnityEditor.EditorUtility.ClearDirty(m);
                return;
            }
#endif
            m.SetFloat(id, v);
        }

        static void SetV(Material m, int id, Vector4 v)
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(m))
            {
                bool wasDirty = UnityEditor.EditorUtility.IsDirty(m);
                m.SetVector(id, v);
                if (!wasDirty) UnityEditor.EditorUtility.ClearDirty(m);
                return;
            }
#endif
            m.SetVector(id, v);
        }

        static void Set(Material m, int id, Texture t)
        {
#if UNITY_EDITOR
            // a material ASSET must not keep a runtime swap (TextureTier's rule)
            if (UnityEditor.EditorUtility.IsPersistent(m))
            {
                bool wasDirty = UnityEditor.EditorUtility.IsDirty(m);
                m.SetTexture(id, t);
                if (!wasDirty) UnityEditor.EditorUtility.ClearDirty(m);
                return;
            }
#endif
            m.SetTexture(id, t);
        }
    }
}
