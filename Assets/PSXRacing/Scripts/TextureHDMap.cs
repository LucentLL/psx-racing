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

        /// <summary>THE ROAD BASE (Charlotte texture batch 01, 2026-11-11): a
        /// Charlotte carriageway texture (city_road_&lt;profile&gt;_&lt;surface&gt;,
        /// lane paint baked over hash grain at 19 cm a texel) drawn instead as
        /// an HD base surface tiled every <see cref="TileM"/> along and across
        /// the ribbon, with the paint laid over it from a mask the import cut
        /// from the original's exact paint texels. <c>paint</c> is the mask's
        /// key (null for a slab with no paint: the junction fans, whose UV is
        /// world XZ / 12 m); <c>baseKey</c> the HD set
        /// (city_road_base_&lt;surface&gt;); <c>repeat</c> how many base tiles
        /// one unit of the original's UV spans (u across, v along).</summary>
        [System.Serializable]
        public struct Road { public Texture2D original; public string paint, baseKey; public Vector2 repeat; }
        public Road[] roads;
        /// <summary>The paint colours the masks stand for (linear): the road
        /// painter's white and yellow, so the lines keep the Classic colours.</summary>
        public Color paintWhite, paintYellow;
        public const float TileM = 4f;
    }
}
