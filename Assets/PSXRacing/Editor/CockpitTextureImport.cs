using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The v08 cockpit's textures (Resources/Cockpit, tools/cockpit/
    /// cockpit_v08_tex.py), imported by path so every sandbox agrees:
    ///   * v08_vinyl / abs / headliner / leather: PACKED DATA (normal X/Y in
    ///     A/G, roughness R, base luminance B) - linear, RGBA, block-compressed
    ///     (DXT5 on WebGL), mipped, bilinear, REPEAT: SurfaceUV tiles every
    ///     0.128 m and the mips are what keep the grain from shimmering;
    ///   * v08_atlas and hands_skin: colour - sRGB, compressed, mipped.
    /// Compressed, so ReleaseBudget's 16-bit set never takes them (it skips
    /// compressed textures), and none of them is point-filtered: this cabin is
    /// the one modern-PBR surface in the game, a metre from the eye.
    /// </summary>
    class CockpitTextureImport : AssetPostprocessor
    {
        internal const string Dir = "Assets/PSXRacing/Resources/Cockpit/";

        void OnPreprocessTexture()
        {
            string p = assetPath.Replace('\\', '/');
            if (!p.StartsWith(Dir) || !p.EndsWith(".png")) return;
            string file = System.IO.Path.GetFileNameWithoutExtension(p);
            bool atlas = file == "v08_atlas" || file == "hands_skin";
            bool surface = !atlas && file.StartsWith("v08_");
            if (!atlas && !surface) return;
            var imp = (TextureImporter)assetImporter;
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = atlas;
            imp.alphaSource = surface ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            imp.alphaIsTransparency = false;
            imp.mipmapEnabled = true;
            imp.mipmapFilter = TextureImporterMipFilter.BoxFilter;
            imp.streamingMipmaps = false;
            imp.filterMode = FilterMode.Trilinear;
            imp.anisoLevel = 2;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.isReadable = false;
            imp.npotScale = TextureImporterNPOTScale.None;
            int size = file == "v08_atlas" ? 1024 : file == "hands_skin" ? 256 : 512;
            imp.maxTextureSize = size;
            imp.textureCompression = TextureImporterCompression.Compressed;
            imp.crunchedCompression = false;
            var wgl = imp.GetPlatformTextureSettings("WebGL");
            wgl.overridden = true;
            wgl.maxTextureSize = size;
            wgl.textureCompression = TextureImporterCompression.Compressed;
            wgl.format = surface ? TextureImporterFormat.DXT5 : TextureImporterFormat.DXT1;
            imp.SetPlatformTextureSettings(wgl);
        }
    }
}
