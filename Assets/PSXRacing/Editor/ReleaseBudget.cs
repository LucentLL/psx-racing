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
    ///    SINCE 2026-09-29 ONLY UNDER Art/LifeSim (SixteenBitRoot): a 565
    ///    texel has no sRGB decode on WebGL2, see SixteenBitCandidate.
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
            int tex16 = 0, texKept = 0, models = 0;
            try
            {
                tex16 = Textures(out texKept);
                models = Models();
            }
            finally { AssetDatabase.StopAssetEditing(); }
            log.Append($" textures to RGB565: {tex16} changed ({texKept} already); prop models: {models} changed;");
            log.Append(" renderers: " + Renderers() + ";");

            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            log.Append(" splash off; wasm: " + CodeOptimization("RuntimeSpeedLTO"));
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
            return log.ToString();
        }

        /// <summary>Where the 16-bit override may go. THE COLOUR HOTFIX
        /// (2026-09-29, the colour plan's C1a): WebGL2 has no sRGB RGB565, and
        /// the project is Linear, so a 565 texel's gamma code reaches the
        /// shader AS linear light - up to x9 brighter on the darkest texels.
        /// The "/Roads/" exclusion below only ever caught Art/Roads; every
        /// venue's road samples an Art/City or Art/GasStation texture, and on
        /// 2026-09-27 every road, deck, car, building and city surface went
        /// 1.5-9x too bright in the player ("I'm not sure what happened to the
        /// color"). Until the shaders decode those texels themselves (C1b),
        /// the override is limited to Art/LifeSim - the interiors, and the
        /// pack buildings the venues' scenery borrows from it, which stay raw
        /// until C1b; 13.4 of the 17.9 MiB it saved - and everything else goes
        /// back to the default import, which the GPU decodes as sRGB.</summary>
        const string SixteenBitRoot = "Assets/PSXRacing/Art/LifeSim/";

        static bool SixteenBitCandidate(string path, TextureImporter imp)
        {
            if (!path.StartsWith("Assets/")) return false;
            string p = path.Replace('\\', '/');
            if (!p.StartsWith(SixteenBitRoot)) return false;
            if (p.Contains("/Roads/") || p.Contains("/Resources/Sky/") || p.Contains("/UI/") || p.Contains("/Fonts/"))
                return false;
            if (imp.textureType != TextureImporterType.Default) return false;   // sprites, normal maps, lightmaps
            if (!imp.sRGBTexture) return false;                                  // linear data
            if (imp.textureCompression == TextureImporterCompression.Compressed) return false;   // the sky's own import
            if (imp.alphaSource != TextureImporterAlphaSource.None && imp.DoesSourceTextureHaveAlpha()) return false;
            return true;
        }

        static int Textures(out int already)
        {
            already = 0;
            int changed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/PSXRacing" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter imp)) continue;
                var s = imp.GetPlatformTextureSettings("WebGL");
                bool want = SixteenBitCandidate(path, imp);
                if (!want)
                {
                    // One this pass set earlier and no longer qualifies: back to
                    // the default (a texture that gained alpha, a new exclusion).
                    if (s.overridden && s.format == TextureImporterFormat.RGB16)
                    {
                        s.overridden = false;
                        imp.SetPlatformTextureSettings(s);
                        imp.SaveAndReimport();
                        changed++;
                    }
                    continue;
                }
                if (s.overridden && s.format == TextureImporterFormat.RGB16 && s.maxTextureSize == imp.maxTextureSize)
                { already++; continue; }
                s.overridden = true;
                s.format = TextureImporterFormat.RGB16;       // RGB565
                s.maxTextureSize = imp.maxTextureSize;
                s.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SetPlatformTextureSettings(s);
                imp.SaveAndReimport();
                changed++;
            }
            return changed;
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
