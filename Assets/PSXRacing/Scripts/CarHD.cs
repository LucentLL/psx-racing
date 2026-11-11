using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE HD CAR TIER (owner, 2026-11-11, over a yellow FD at a seller's
    /// driveway: "why are body panel lines so smeared? I used bitmap for these
    /// to give the cars depth, but in game they are just blurry lines that
    /// fade away" - and, offered this tier: "this should have been implemented
    /// a while ago when the graphics goal transitioned to PS3/PS4 quality").
    ///
    /// The cause: every car is drawn from ONE 512 x 256 colour sheet, point
    /// sampled with no mips (CarModelBaker.PointFilter), lit from vertex
    /// normals only. The artist's 2048 x 1024 sheets were box-filtered 4x to
    /// make it, so a 2 px shut line became half a texel and averaged into a
    /// faint smear, and the groove's depth - which lives in the tangent-space
    /// normal map - was never read.
    ///
    /// On the PC profile, a PSX/CarPaint material whose sheet has an HD set
    /// gets: the full-size colour sheet in _MainTex (BC7, mipped, trilinear,
    /// 16x anisotropic), the normal map (_CarHDNormal, BC5), the packed
    /// AO / roughness / metallic / coat map (_CarHDORM) and the PSX_CAR_HD
    /// keyword (PSXCarPaint.shader: lighting off the mapped normal, AO on the
    /// sky light and the reflection, a rough texel loses its lacquer). And
    /// <see cref="LampMap"/> hands CarLights the body's lamp map at the same
    /// size. Everything else the paint does is the shader's own and reads the
    /// same inputs (glass by luminance, matte by alpha, DullWheels, lamps,
    /// rain and snow, the reflection), so it survives untouched.
    ///
    /// WHERE: inside PSXTexDecode.Stamp, after the environment's tier - and
    /// every car shell in the game comes through it, because the liveries are
    /// SHARED material assets that CarModelLibrary.Load stamps the moment a
    /// body is first asked for (the grid, traffic, AI, the garage, the car
    /// viewer, the seller's drive, the dealer, the junkyard, replays and the
    /// inspection all take their material from CarModelDef.skinMaterials).
    /// The sets load then, per body, from <see cref="BundleFile"/> beside the
    /// player (never in the project); nothing for a body that never appears.
    ///
    /// A/B: -psx-no-car-hd in the player; PSX_CAR_HD=1 turns it on in the
    /// editor (it is off there, like PSX_HD, so a tool frame is the web
    /// player's unless asked).
    /// </summary>
    public static class CarHD
    {
        public const string BundleFile = "carhd.bundle";
#if UNITY_EDITOR
        public const string EditorSrcRoot = "Assets/PSXRacing/HDSrc/CarHD/";
#endif
        public const string Keyword = "PSX_CAR_HD";
        public const string NormalSuffix = "_normal", OrmSuffix = "_orm";

        struct HDSet { public Texture2D col, nrm, orm; }
        static readonly Dictionary<string, Texture2D> loadedByName = new Dictionary<string, Texture2D>();
        /// <summary>One load per bundle name: shared normal / ORM / lamp maps are one texture.</summary>
        static Texture2D LoadShared(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (loadedByName.TryGetValue(name, out var t) && t != null) return t;
            t = Load(name);
            loadedByName[name] = t;
            return t;
        }

        static CarHDMap map;
        static bool mapTried, bundleTried;
        static AssetBundle bundle;
        static readonly Dictionary<Texture, int> byOriginal = new Dictionary<Texture, int>();
        static readonly Dictionary<Texture, int> byHD = new Dictionary<Texture, int>();
        static readonly Dictionary<string, Texture2D> lampByBody = new Dictionary<string, Texture2D>();
        static HDSet[] sets;
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int NormalId = Shader.PropertyToID("_CarHDNormal"), OrmId = Shader.PropertyToID("_CarHDORM");

        /// <summary>TOOLS: force the tier off whatever the profile says (an A/B
        /// in one editor run). Set, then PSXTexDecode.StampAll().</summary>
        public static bool ForceOff;

        /// <summary>The tier is wanted: the PC profile, not refused.</summary>
        public static bool Want
        {
            get
            {
                if (ForceOff) return false;
#if UNITY_EDITOR
                return System.Environment.GetEnvironmentVariable("PSX_CAR_HD") == "1";
#else
                return PlatformProfile.PC && !NoArg;
#endif
            }
        }
        static int noArg = -1;
        static bool NoArg
        {
            get
            {
                if (noArg < 0)
                {
                    noArg = 0;
                    foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-psx-no-car-hd") noArg = 1;
                }
                return noArg == 1;
            }
        }

        public static int SheetCount => map != null && map.sheets != null ? map.sheets.Length : 0;
        /// <summary>Sets loaded so far (each colour + normal + ORM).</summary>
        public static int Loaded { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (!Want) return;
            if (EnsureMap() && EnsureBundle())
                Debug.Log($"[CarHD] HD car tier on: {SheetCount} livery sheets, {(map.lamps != null ? map.lamps.Length : 0)} lamp maps in {BundleFile}");
        }

        /// <summary>Forget the table (the editor, after CarHDTool.Import).</summary>
        public static void ResetMap()
        {
            map = null; mapTried = false; sets = null; Loaded = 0;
            byOriginal.Clear(); byHD.Clear(); lampByBody.Clear(); loadedByName.Clear();
        }

        static bool EnsureMap()
        {
            if (map != null) return true;
            if (mapTried) return false;
            mapTried = true;
            map = Resources.Load<CarHDMap>(CarHDMap.ResourcePath);
            if (map == null) return false;
            if (map.sheets == null) map.sheets = new CarHDMap.Sheet[0];
            if (map.lamps == null) map.lamps = new CarHDMap.Lamp[0];
            if (map.sheets.Length == 0) { map = null; return false; }
            sets = new HDSet[map.sheets.Length];
            for (int i = 0; i < map.sheets.Length; i++)
                if (map.sheets[i].original != null) byOriginal[map.sheets[i].original] = i;
            return true;
        }

        static bool EnsureBundle()
        {
#if UNITY_EDITOR
            return true;   // the editor reads HDSrc/CarHD
#else
            if (bundle != null) return true;
            if (bundleTried) return false;
            bundleTried = true;
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, BundleFile);
            if (!System.IO.File.Exists(path)) { Debug.Log("[CarHD] no " + BundleFile + " beside the player - HD car tier off"); return false; }
            bundle = AssetBundle.LoadFromFile(path);   // the header now, each texture when a body first asks
            if (bundle == null) Debug.LogWarning("[CarHD] " + BundleFile + " did not load - HD car tier off");
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
            var sh = map.sheets[i];
            string key = sh.key;
            float t0 = Time.realtimeSinceStartup;
            s.col = Load(key);
            if (s.col == null) return false;
            s.nrm = LoadShared(string.IsNullOrEmpty(sh.normalKey) ? key + NormalSuffix : sh.normalKey);
            s.orm = LoadShared(string.IsNullOrEmpty(sh.ormKey) ? key + OrmSuffix : sh.ormKey);
            sets[i] = s;
            byHD[s.col] = i;
            Loaded++;
            Debug.Log($"[CarHD] {key}: loaded in {(Time.realtimeSinceStartup - t0) * 1000f:0} ms (normal {(s.nrm != null)}, orm {(s.orm != null)})");
            return true;
        }

        /// <summary>The HD lamp control map for a body, or null (no tier, or
        /// no HD map for it). CarLights.LampMapFor asks this first.</summary>
        public static Texture2D LampMap(string body)
        {
            if (string.IsNullOrEmpty(body) || !Want || !EnsureMap() || !EnsureBundle()) return null;
            if (lampByBody.TryGetValue(body, out var t)) return t;
            t = null;
            foreach (var l in map.lamps) if (l.body == body) { t = LoadShared(l.key); break; }
            lampByBody[body] = t;
            return t;
        }

        /// <summary>TOOLS: one line on why a material is or is not in the tier.</summary>
        public static string Diag(Material m)
        {
            if (m == null) return "no material";
            var main = m.GetTexture(MainTexId);
            bool mapOk = EnsureMap();
            var to = main != null ? TextureTier.OriginalOf(main) : null;
            return $"shader {m.shader.name} hasNormalProp {m.HasProperty(NormalId)} want {Want} map {mapOk} sheets {SheetCount} " +
                   $"main {(main != null ? main.name + "#" + main.GetHashCode() : "null")} inMap {(to != null && byOriginal.ContainsKey(to))} " +
                   $"isHD {(main != null && byHD.ContainsKey(main))} tierOrig {(to != null ? to.name + "#" + to.GetHashCode() : "-")} kw {m.IsKeywordEnabled(Keyword)} loaded {Loaded}" +
                   (map != null && map.sheets.Length > 0 ? $" first original {(map.sheets[0].original != null ? map.sheets[0].original.name + "#" + map.sheets[0].original.GetHashCode() : "NULL")}" : "");
        }

        /// <summary>The 512 sheet an HD colour sheet stands for (or the
        /// texture itself): the tools' and the swatch's question.</summary>
        public static Texture OriginalOf(Texture t)
        {
            if (t != null && map != null && byHD.TryGetValue(t, out int i) && map.sheets[i].original != null) return map.sheets[i].original;
            return t;
        }

        /// <summary>Put the HD set on one PSX/CarPaint material, or take it off.
        /// True when anything changed. Called by PSXTexDecode.Stamp.</summary>
        public static bool Apply(Material m)
        {
            if (m == null || !m.HasProperty(NormalId)) return false;   // not PSX/CarPaint
            bool changed = false;
            bool hdReady = Want && EnsureMap() && EnsureBundle();
            var main = m.GetTexture(MainTexId);
            // a smoothed Classic sheet (SmoothSheets) stands for its original
            if (main != null && smoothToOrig.TryGetValue(main, out var so))
            {
                if (SmoothSheets && !(hdReady && byOriginal.ContainsKey(so))) return false;
                Set(m, MainTexId, so);
                main = so;
                changed = true;
            }
            bool on = m.IsKeywordEnabled(Keyword);
            if (!hdReady)
            {
                if (on) { Off(m); changed = true; main = m.GetTexture(MainTexId); }
            }
            else if (main != null)
            {
                if (on && byHD.ContainsKey(main)) return changed;   // already HD
                // (the sheet may wear its Classic/Enhanced twin: asked by its original)
                var orig = TextureTier.OriginalOf(main);
                if (orig != null && byOriginal.TryGetValue(orig, out int i) && Get(i, out var s))
                {
                    Set(m, MainTexId, s.col);
                    if (s.nrm != null) Set(m, NormalId, s.nrm);
                    if (s.orm != null) Set(m, OrmId, s.orm);
                    Kw(m, s.nrm != null && s.orm != null);
                    return true;
                }
                // a material COPIED from an HD livery and given another texture
                // (CarShell's jack stands): not this car's maps any more
                if (on) { Kw(m, false); changed = true; }
            }
            // A Classic sheet (no HD set): point sampled as it ships, or - the
            // A/B, SmoothSheets - a mipped trilinear copy.
            if (SmoothSheets && main is Texture2D t2 && !byHD.ContainsKey(main))
            {
                var sm = Smooth(t2);
                if (sm != null) { Set(m, MainTexId, sm); changed = true; }
            }
            return changed;
        }

        // ---- OLDER CARS (no HD set): the PC profile's choice for a 128 / 512 sheet ----
        /// <summary>
        /// A Classic car sheet drawn from a MIPPED, TRILINEAR copy instead of
        /// the point-sampled original (PC only, OFF by default: the A/B for
        /// cars with no HD set; -psx-car-sheet-smooth in the player,
        /// PSX_CAR_SHEET_SMOOTH=1 in the editor). The copy is made once per
        /// sheet on the GPU and read back (the sheets import unreadable): raw
        /// 16-bit codes stay raw (the shader still decodes them), a 565 sheet
        /// is moved to its bins' centres first, exactly as PSXTexDecode's flag
        /// 2 does, because the copy is RGBA32 and gets flag 1.
        /// </summary>
        public static bool SmoothSheets
        {
            get
            {
                if (SmoothOverride >= 0) return SmoothOverride == 1;
#if UNITY_EDITOR
                return System.Environment.GetEnvironmentVariable("PSX_CAR_SHEET_SMOOTH") == "1";
#else
                if (smoothArg < 0)
                {
                    smoothArg = 0;
                    if (PlatformProfile.PC)
                        foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-psx-car-sheet-smooth") smoothArg = 1;
                }
                return smoothArg == 1;
#endif
            }
        }
        /// <summary>TOOLS: -1 = the switch as above, 0 off, 1 on.</summary>
        public static int SmoothOverride = -1;
        static int smoothArg = -1;
        static readonly Dictionary<Texture, Texture2D> smoothOf = new Dictionary<Texture, Texture2D>();
        static readonly Dictionary<Texture, Texture> smoothToOrig = new Dictionary<Texture, Texture>();

        static Texture2D Smooth(Texture2D src)
        {
            if (smoothOf.TryGetValue(src, out var c)) return c;
            if (src.width > 2048 || src.height > 2048) return null;
            bool raw = !src.isDataSRGB;
            int w = src.width, h = src.height;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32,
                                                raw ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            var oldF = src.filterMode;
            src.filterMode = FilterMode.Point;
            Graphics.Blit(src, rt);
            src.filterMode = oldF;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, raw);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            if (src.format == TextureFormat.RGB565)
            {
                var px = t.GetPixels32();
                for (int k = 0; k < px.Length; k++)
                {
                    var p = px[k];
                    px[k] = new Color32((byte)Mathf.Min(255, Mathf.RoundToInt(p.r * 248f / 255f + 3.5f)),
                                        (byte)Mathf.Min(255, Mathf.RoundToInt(p.g * 252f / 255f + 1.5f)),
                                        (byte)Mathf.Min(255, Mathf.RoundToInt(p.b * 248f / 255f + 3.5f)), p.a);
                }
                t.SetPixels32(px);
            }
            t.wrapMode = src.wrapMode;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 8;
            t.name = src.name + "_smooth";
            t.Apply(true, true);
            smoothOf[src] = t;
            smoothToOrig[t] = src;
            return t;
        }

        static void Off(Material m)
        {
            var main = m.GetTexture(MainTexId);
            int i = -1;
            if (main != null && map != null && !byHD.TryGetValue(main, out i))
            {
                i = -1;
                for (int k = 0; k < map.sheets.Length; k++) if (map.sheets[k].key == main.name) { i = k; break; }
            }
            if (i >= 0 && map.sheets[i].original != null) Set(m, MainTexId, map.sheets[i].original);
            Kw(m, false);
        }

        /// <summary>Take the tier off every car material in memory (an A/B in
        /// the editor; then PSXTexDecode.StampAll puts back what Want says).</summary>
        public static int OffAll()
        {
            int n = 0;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && m.HasProperty(NormalId) && m.IsKeywordEnabled(Keyword)) { Off(m); n++; }
            lampByBody.Clear();
            return n;
        }

        static void Kw(Material m, bool on)
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(m))
            {
                bool wasDirty = UnityEditor.EditorUtility.IsDirty(m);
                if (on) m.EnableKeyword(Keyword); else m.DisableKeyword(Keyword);
                if (!wasDirty) UnityEditor.EditorUtility.ClearDirty(m);
                return;
            }
#endif
            if (on) m.EnableKeyword(Keyword); else m.DisableKeyword(Keyword);
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
