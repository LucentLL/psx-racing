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
    /// The twin is held by PATH and loaded only when Enhanced is picked, so a
    /// phone on CLASSIC never loads the 2x set. Built by the editor tool
    /// (PSX Racing/Textures/Build Enhanced Map) from the owner's registry;
    /// never edited by hand.
    /// </summary>
    public class TextureTierMap : ScriptableObject
    {
        public const string ResourcePath = "TextureTierMap";

        [Serializable]
        public struct Pair
        {
            public Texture2D original;
            /// <summary>Resources path of the Enhanced twin (no extension).</summary>
            public string enhanced;
            /// <summary>The registry's stable ID, for logs.</summary>
            public string id;
        }

        public Pair[] pairs = new Pair[0];
    }
}
