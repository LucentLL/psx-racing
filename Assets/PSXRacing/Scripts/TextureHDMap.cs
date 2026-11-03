using UnityEngine;

namespace PSXRacing
{
    /// <summary>The HD tier's table: each original game texture that has an
    /// HD PBR set, and the set's key (the original's basename) in the bundle.
    /// Built by Editor/TextureHDTool in the PC build's sandbox; never committed.</summary>
    public class TextureHDMap : ScriptableObject
    {
        public const string ResourcePath = "TextureHDMap";
        [System.Serializable]
        public struct Pair { public Texture2D original; public string key; }
        public Pair[] pairs;
    }
}
