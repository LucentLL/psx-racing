using System;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// CLASSIC / ENHANCED (owner, 2026-10-11): each original texture that has
    /// an Enhanced twin, and where the twin lives under Resources.
    ///
    /// The original is held by REFERENCE - at runtime a texture has no GUID,
    /// and names collide (two different ConcreteBare sheets, five Roof_tiles).
    /// The twin is held by KEY and loaded only when Enhanced is picked, so a
    /// phone on CLASSIC never loads the 2x set. Built by the editor tool
    /// (PSX Racing/Textures/Build Enhanced Map) from the owner's registry;
    /// never edited by hand.
    ///
    /// The twins are NOT in the player (2026-10-11: they took MAIN's data
    /// file to 106.6 MB, and GitHub refuses 100). They live in
    /// Assets/PSXRacing/EnhancedSrc and ship as one AssetBundle in
    /// StreamingAssets (TextureTier.BundleFile), downloaded only when
    /// ENHANCED is on. The key is the twin's path under EnhancedSrc with no
    /// extension, and the bundle's addressable name is that same key.
    /// </summary>
    public class TextureTierMap : ScriptableObject
    {
        public const string ResourcePath = "TextureTierMap";

        [Serializable]
        public struct Pair
        {
            public Texture2D original;
            /// <summary>The Enhanced twin's key: its path under
            /// Assets/PSXRacing/EnhancedSrc with no extension (e.g.
            /// TexEnhanced/City/road_asphalt_new), and its name in the bundle.</summary>
            public string enhanced;
            /// <summary>The registry's stable ID, for logs.</summary>
            public string id;
        }

        public Pair[] pairs = new Pair[0];

        /// <summary>The bundle's content hash, written by the bundle build
        /// right before each player build (TextureTierTool.BuildBundle) and
        /// sent as ?v= so a browser never reuses an older bundle. Empty =
        /// no bundle built yet (the URL goes out bare).</summary>
        public string bundleVersion = "";
    }
}
