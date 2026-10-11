using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// TEXTURES: ENHANCED / CLASSIC (owner, 2026-10-11: "overhauled textures
    /// that should be used at highest graphical setting. the current/lower
    /// resolution textures can be used at lower visual settings").
    ///
    /// ENHANCED is the owner's 2x art; CLASSIC is the untouched originals.
    /// Defaults: ENHANCED on a desktop, CLASSIC on a phone (the 2x set is 4x
    /// the texels, and a phone browser is where memory runs out).
    /// </summary>
    public static class TexturePrefs
    {
        const string PrefKey = "psx.texEnhanced";
        static int cached = -2;   // -1 = never chosen: follow the device

        public static bool Enhanced
        {
            get
            {
                if (cached == -2) cached = PlayerPrefs.GetInt(PrefKey, -1);
                return cached < 0 ? !Application.isMobilePlatform : cached != 0;
            }
            set
            {
                if (Enhanced == value && cached >= 0) return;
                cached = value ? 1 : 0;
                PlayerPrefs.SetInt(PrefKey, cached);
                PlayerPrefs.Save();
                Changed++;
                TextureTier.ApplyAll();
            }
        }

        public static int Changed { get; private set; }
        public static void Toggle() => Enhanced = !Enhanced;
        public static string Label => Enhanced ? "ENHANCED" : "CLASSIC";
    }

    /// <summary>
    /// The switch itself: every texture slot of a material that holds a mapped
    /// original gets the Enhanced twin, or back. It works on the SHARED
    /// material assets, so every renderer that wears one follows, later
    /// spawns included, and runtime copies made from them (the skyline's, the
    /// shadow casters') are swept with everything else in memory.
    ///
    /// It rides on <see cref="PSXTexDecode.Stamp"/>, which already runs on
    /// every material the game shows before it is drawn - scene loads, the
    /// Resources prefabs, CityKit, the season's dresses, the car dressers -
    /// and must run AFTER a swap anyway, because the 16-bit decode flag is
    /// read off the texture the slot holds.
    ///
    /// The editor never keeps a swap: play mode puts the originals back on
    /// exit, and the .mat save guard puts them back before any save. In edit
    /// mode the batch screenshot tools can ask for ENHANCED with
    /// PSX_TEX=enhanced (that process only).
    /// </summary>
    public static class TextureTier
    {
        static TextureTierMap map;
        static bool mapTried;
        static readonly Dictionary<Texture, int> byOriginal = new Dictionary<Texture, int>();
        static readonly Dictionary<Texture, int> byEnhanced = new Dictionary<Texture, int>();
        static Texture2D[] loaded;
        static int loadedCount;
        static readonly List<int> ids = new List<int>();

        /// <summary>The tier materials should show right now.</summary>
        public static bool WantEnhanced
        {
            get
            {
#if UNITY_EDITOR
                if (ForceClassic) return false;
                if (!Application.isPlaying)
                    return System.Environment.GetEnvironmentVariable("PSX_TEX") == "enhanced";
#endif
                return TexturePrefs.Enhanced;
            }
        }

#if UNITY_EDITOR
        /// <summary>Set by the editor while it restores the originals.</summary>
        public static bool ForceClassic;
#endif

        static bool EnsureMap()
        {
            if (map != null) return true;
            if (mapTried) return false;
            mapTried = true;
            map = Resources.Load<TextureTierMap>(TextureTierMap.ResourcePath);
            if (map == null || map.pairs == null) { map = null; return false; }
            loaded = new Texture2D[map.pairs.Length];
            for (int i = 0; i < map.pairs.Length; i++)
                if (map.pairs[i].original != null) byOriginal[map.pairs[i].original] = i;
            return true;
        }

        /// <summary>The Enhanced twin of pair i, loaded on first use; null
        /// (keep the original) when it is missing.</summary>
        static Texture2D Twin(int i)
        {
            if (loaded[i] != null) return loaded[i];
            var t = Resources.Load<Texture2D>(map.pairs[i].enhanced);
            if (t == null) return null;
            loaded[i] = t;
            loadedCount++;
            byEnhanced[t] = i;
            return t;
        }

        /// <summary>Put the wanted tier's textures in this material's slots.
        /// True when a slot changed. Cheap when nothing is Enhanced: CLASSIC
        /// with no twin ever loaded never even loads the map.</summary>
        public static bool Swap(Material m)
        {
            if (m == null) return false;
            bool want = WantEnhanced;
            if (!want && loadedCount == 0) return false;
            if (!EnsureMap()) return false;
            ids.Clear();
            m.GetTexturePropertyNameIDs(ids);
            bool changed = false;
            for (int k = 0; k < ids.Count; k++)
            {
                var t = m.GetTexture(ids[k]);
                if (t == null) continue;
                Texture put = null;
                if (want)
                {
                    if (byOriginal.TryGetValue(t, out int i)) put = Twin(i);
                }
                else if (byEnhanced.TryGetValue(t, out int j)) put = map.pairs[j].original;
                if (put == null || put == t) continue;
                Set(m, ids[k], put);
                changed = true;
            }
            return changed;
        }

        static void Set(Material m, int id, Texture t)
        {
#if UNITY_EDITOR
            // A material ASSET swapped in the editor must not become a saved
            // value (the same rule PSXTexDecode keeps for its flag).
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

        /// <summary>Every material in memory to the wanted tier (and its
        /// decode flag after it); on CLASSIC the twins are then released, so
        /// switching back really gives the memory back.</summary>
        public static void ApplyAll()
        {
            PSXTexDecode.StampAll();
            if (!WantEnhanced) Release();
        }

        static void Release()
        {
            if (loadedCount == 0 || loaded == null) return;
            for (int i = 0; i < loaded.Length; i++)
            {
                if (loaded[i] == null) continue;
                Resources.UnloadAsset(loaded[i]);
                loaded[i] = null;
            }
            byEnhanced.Clear();
            loadedCount = 0;
        }

        /// <summary>How many Enhanced twins are in memory (for logs/tests).</summary>
        public static int LoadedCount => loadedCount;

        /// <summary>The map (editor tools and the self-test).</summary>
        public static TextureTierMap Map => EnsureMap() ? map : null;

        /// <summary>Forget the map so a rebuilt one is read again.</summary>
        public static void ResetMap()
        {
            Release();
            map = null;
            mapTried = false;
            byOriginal.Clear();
        }
    }
}
