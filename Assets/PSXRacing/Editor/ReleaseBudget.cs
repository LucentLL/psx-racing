using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE RELEASE BUDGET: what the WebGL player carries, set once before every
    /// build (PSXBuildWebGL.Run) and guarded so nothing that is already right is
    /// reimported. The shipped WebGL.data stood at 99.75 MiB on 2026-09-27
    /// against GitHub's 100 MiB file limit, with every texture uncompressed
    /// 32-bit and the prop models carrying tangents no shader reads. The owner:
    /// "optimize as would be best for a game release with solid performance".
    ///
    ///  * OPAQUE COLOUR TEXTURES SHIP AS RGB565 (WebGL override only; the
    ///    editor keeps the source). The frame is quantized to 5 bits a channel
    ///    with a Bayer dither (PSX/Blit) - PS1 output - so a 16-bit texture is
    ///    what the console itself sampled, and it halves both the download and
    ///    the texture memory a phone holds. Not: anything with alpha (cut-out
    ///    trees, decals), linear data textures, sprites (UI is drawn after the
    ///    quantizer), the sky (its own compressed, mipped import), and the ROAD
    ///    textures - the road's colours are the owner's.
    ///    WebGL2 has NO sRGB 565: since 2026-09-29 the set is imported as
    ///    linear data everywhere and the shaders decode it (Psx16Label).
    ///  * PROP MODELS: no tangents (no PSX shader has a TANGENT input) and
    ///    Medium mesh compression (16-bit positions over the model's own
    ///    bounds - under a millimetre on a house). Cars are left exact.
    ///  * NO URP POST DATA on the two renderers: no shipped camera enables URP
    ///    post-processing (the PSX look is its own renderer features), and the
    ///    post data dragged its film-grain and SMAA textures and the uber-post
    ///    shaders into every build.
    ///  * No Unity splash; the wasm built for runtime speed with LTO.
    /// </summary>
    public static class ReleaseBudget
    {
        /// <summary>Folders under Assets/PSXRacing/Art whose models are props
        /// (scenery and buildings nobody's wheel reads as a road).</summary>
        static readonly string[] PropModelDirs =
        {
            "Assets/PSXRacing/Art/LifeSim", "Assets/PSXRacing/Art/GasStation", "Assets/PSXRacing/Art/Buildings",
            "Assets/PSXRacing/Art/City", "Assets/PSXRacing/Art/Beach", "Assets/PSXRacing/Art/Bogue",
            "Assets/PSXRacing/Art/Roadside",
        };

        public static string Apply()
        {
            var log = new StringBuilder("[ReleaseBudget]");
            AssetDatabase.StartAssetEditing();
            int tex16 = 0, texKept = 0, texSet = 0, tex565 = 0, models = 0;
            try
            {
                tex16 = Textures(out texKept, out texSet, out tex565);
                models = Models();
            }
            finally { AssetDatabase.StopAssetEditing(); }
            log.Append($" 16-bit set ({Psx16Label}, linear, decoded in the shader): {texSet} textures, {tex565} RGB565 on WebGL;" +
                       $" {tex16} changed ({texKept} already right); prop models: {models} changed;");
            log.Append(" renderers: " + Renderers() + ";");

            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            log.Append(" splash off; wasm: " + CodeOptimization("RuntimeSpeedLTO"));
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
            return log.ToString();
        }

        /// <summary>
        /// THE 16-BIT SET, AND WHY IT IS IMPORTED AS LINEAR DATA (the colour
        /// pass, 2026-09-29).
        ///
        /// WebGL2 has no sRGB RGB565 format, and the project renders in Linear.
        /// From 2026-09-27 to 09-29 the set shipped as 565 with the sRGB import
        /// flag on, so the GPU handed each texel's GAMMA code to the shader as
        /// linear light: fresh asphalt x9, old asphalt x4.8, concrete x1.5-2.1,
        /// every car and building likewise ("I'm not sure what happened to the
        /// color"). The "/Roads/" exclusion below only ever caught Art/Roads -
        /// every venue's road samples Art/City or Art/GasStation.
        ///
        /// C1a limited the override to the LifeSim interiors. C1b (this) makes
        /// the set honest on every target instead: each texture of it carries
        /// the asset label <see cref="Psx16Label"/> and is imported with
        /// sRGBTexture OFF on EVERY platform, so no GPU decodes it anywhere
        /// (RGB24/RGBA32 on Standalone and in the editor, RGB565 on WebGL) and
        /// the PSX shaders decode it themselves with the exact sRGB curve
        /// (Shaders/PSXTexDecode.cginc), switched per material by
        /// Scripts/PSXTexDecode.cs from Texture.isDataSRGB. The label is what
        /// keeps a rerun from mistaking the set for real linear data (the
        /// "!sRGBTexture" exclusion) now that the set IS linear on import.
        /// </summary>
        public const string Psx16Label = "psx16";

        /// <summary>Whether the RGB565 WebGL override covers the whole set or
        /// only the LifeSim interiors. The set itself (label, linear import,
        /// shader decode) is the same either way; this is only the download.
        /// C1b stage 1 shipped it false (every outdoor texture RGBA32 until
        /// the decode audit passed), stage 2 true.</summary>
        internal const bool SixteenBitEverywhere = true;
        const string LifeSimRoot = "Assets/PSXRacing/Art/LifeSim/";

        /// <summary>A texture of the 16-bit set: an opaque, uncompressed,
        /// ordinary colour texture of the game's own art. Every one of them is
        /// point-filtered with no mips, which is what makes the shader's
        /// decode exact.</summary>
        internal static bool InSet(string path, TextureImporter imp, bool labelled)
        {
            if (!path.StartsWith("Assets/")) return false;
            string p = path.Replace('\\', '/');
            if (p.Contains("/Roads/") || p.Contains("/Resources/Sky/") || p.Contains("/UI/") || p.Contains("/Fonts/"))
                return false;
            if (imp.textureType != TextureImporterType.Default) return false;   // sprites, normal maps, lightmaps
            // Linear data (masks, noise) - unless it is linear because it is
            // one of ours: the label says so.
            if (!imp.sRGBTexture && !labelled) return false;
            if (imp.textureCompression == TextureImporterCompression.Compressed) return false;   // the sky's own import
            if (imp.alphaSource != TextureImporterAlphaSource.None && imp.DoesSourceTextureHaveAlpha()) return false;
            return true;
        }

        static bool Wants565(string path) =>
            SixteenBitEverywhere || path.Replace('\\', '/').StartsWith(LifeSimRoot);

        internal static bool HasPsx16Label(string guid) =>
            Array.IndexOf(AssetDatabase.GetLabels(new GUID(guid)), Psx16Label) >= 0;

        static int Textures(out int already, out int inSet, out int in565)
        {
            already = 0; inSet = 0; in565 = 0;
            int changed = 0;
            var label = new List<string>();     // paths to gain the label
            var unlabel = new List<string>();   // paths to lose it
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/PSXRacing" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter imp)) continue;
                bool labelled = HasPsx16Label(guid);
                bool set = InSet(path, imp, labelled);
                bool want565 = set && Wants565(path);
                if (set) inSet++;
                if (want565) in565++;

                bool dirty = false;
                if (set)
                {
                    if (imp.sRGBTexture) { imp.sRGBTexture = false; dirty = true; }   // decoded in the shader
                    if (!labelled) label.Add(path);
                }
                else if (labelled)
                {
                    // One of ours that no longer qualifies (it gained alpha, a
                    // new exclusion): a colour texture again, decoded by the GPU.
                    imp.sRGBTexture = true; dirty = true;
                    unlabel.Add(path);
                }

                var s = imp.GetPlatformTextureSettings("WebGL");
                bool has565 = s.overridden && s.format == TextureImporterFormat.RGB16;
                if (want565 && !(has565 && s.maxTextureSize == imp.maxTextureSize))
                {
                    s.overridden = true;
                    s.format = TextureImporterFormat.RGB16;       // RGB565
                    s.maxTextureSize = imp.maxTextureSize;
                    s.textureCompression = TextureImporterCompression.Uncompressed;
                    imp.SetPlatformTextureSettings(s);
                    dirty = true;
                }
                else if (!want565 && has565)
                {
                    s.overridden = false;
                    imp.SetPlatformTextureSettings(s);
                    dirty = true;
                }

                if (dirty) { imp.SaveAndReimport(); changed++; }
                else if (set && labelled) already++;
            }
            // Labels live in the .meta and need the asset object; they change
            // no import, so they are written after the reimports.
            foreach (var path in label) SetPsx16Label(path, true);
            foreach (var path in unlabel) SetPsx16Label(path, false);
            return changed + label.Count + unlabel.Count;
        }

        static void SetPsx16Label(string path, bool on)
        {
            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj == null) return;
            var have = new List<string>(AssetDatabase.GetLabels(obj));
            if (on == have.Contains(Psx16Label)) return;
            if (on) have.Add(Psx16Label); else have.Remove(Psx16Label);
            AssetDatabase.SetLabels(obj, have.ToArray());
        }

        /// <summary>The budget without a build: -executeMethod
        /// PSXRacing.EditorTools.ReleaseBudget.ApplyFromCommandLine. For a
        /// sandbox whose frames or audits must see the imports a build would
        /// ship. Writes PSXRacing_release_budget.txt.</summary>
        public static void ApplyFromCommandLine()
        {
            string said;
            try { said = Apply(); }
            catch (Exception e) { said = "[ReleaseBudget] THREW " + e; }
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "PSXRacing_release_budget.txt"), said + "\n");
            EditorApplication.Exit(said.Contains("THREW") ? 1 : 0);
        }

        static int Models()
        {
            int changed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/PSXRacing/Art" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) continue;
                bool prop = Array.Exists(PropModelDirs, d => path.StartsWith(d + "/"));
                var wantComp = prop ? ModelImporterMeshCompression.Medium : mi.meshCompression;
                if (mi.importTangents == ModelImporterTangents.None && mi.meshCompression == wantComp) continue;
                mi.importTangents = ModelImporterTangents.None;
                mi.meshCompression = wantComp;
                mi.SaveAndReimport();
                changed++;
            }
            return changed;
        }

        static string Renderers()
        {
            var said = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets/Settings" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var r = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (r == null) continue;
                if (r.postProcessData == null) { said.Add(r.name + " already"); continue; }
                r.postProcessData = null;
                EditorUtility.SetDirty(r);
                said.Add(r.name + " post data removed");
            }
            return said.Count == 0 ? "none found" : string.Join(", ", said);
        }

        /// <summary>UnityEditor.WebGL.UserBuildSettings.codeOptimization, by
        /// reflection: the WebGL editor extension is only loaded with the WebGL
        /// module, and a rename there must not stop a build.</summary>
        static string CodeOptimization(string value)
        {
            try
            {
                Type t = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = a.GetType("UnityEditor.WebGL.UserBuildSettings");
                    if (t != null) break;
                }
                var prop = t?.GetProperty("codeOptimization", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (prop == null) return "setting not found (left as is)";
                prop.SetValue(null, Enum.Parse(prop.PropertyType, value));
                return value;
            }
            catch (Exception e) { return "not set (" + e.GetType().Name + ")"; }
        }
    }
}
