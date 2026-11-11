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
        public struct Road { public Texture2D original; public string paint, baseKey; public Vector2 repeat; public float widthM; public bool gutter; }
        public Road[] roads;

        /// <summary>Round 2 (2026-11-11): Codex's next batch, bound by the shader
        /// with no game texture of their own. <c>facadeParts</c>: the facade
        /// atlas's column 7 at 512 x 2048 (the column's own layout, 4x across,
        /// 8x up). <c>gutter</c>: the concrete gutter pan along a curbed
        /// street's edges, 4 m along x 1 m across (y 0 at the curb).
        /// Null when the set is not delivered.</summary>
        public string facadeParts, gutter;
        public const string FacadePartsKey = "city_facade_parts", GutterKey = "city_gutter_pan";
        public const float GutterAlongM = 4f, GutterAcrossM = 1f;

        /// <summary>Round 3 (2026-11-11): THE FACADE ATLAS IN HD, per column.
        /// Codex delivers <c>city_facade_cNN</c> (NN = the atlas column 00-15,
        /// 1024 x 2048: the column 8x across and 8x up) with colour, normal,
        /// rough, ao and an optional <c>_glass</c> mask; the importer stacks them
        /// into three Texture2DArrays (colour; normal; rough/ao/glass packed),
        /// one slice per column, and <see cref="facadeCols"/> says which slices
        /// hold art. Column 7 stays <see cref="facadeParts"/>.</summary>
        public bool[] facadeCols;
        public const string FacadeColPrefix = "city_facade_c", FacadeArrayKey = "city_facade_hd";
        public const int FacadeCols = 16, FacadeSliceW = 1024, FacadeSliceH = 2048;

        /// <summary>THE BLACK ROAD IN SHADE (round 2, measured): R1 kept the old
        /// palette's fresh-asphalt albedo, linear 0.013 (sRGB 30) - a quarter
        /// of real fresh asphalt's ~0.04-0.05 - so wherever a tower shades an
        /// Uptown street the road is 1/24 of the shaded concrete beside it and
        /// reads as black with paint floating on it (sun maps off: road/plaza
        /// 0.055; sun on, in shade: 0.042; the sky map changes nothing). The
        /// base set of that surface is drawn this much brighter (paint not);
        /// 1 everywhere else. Codex is asked to author asphalt_new at ~0.04 so
        /// this can return to 1.</summary>
        public static float BaseGain(string baseKey) =>
            baseKey != null && baseKey.EndsWith("asphalt_new") ? AsphaltNewGain : 1f;
        public static float AsphaltNewGain = 3.1f;
        /// <summary>The paint colours the masks stand for (linear): the road
        /// painter's white and yellow, so the lines keep the Classic colours.</summary>
        public Color paintWhite, paintYellow;
        public const float TileM = 4f;
    }
}
