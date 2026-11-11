using UnityEngine;

namespace PSXRacing
{
    /// <summary>THE HD CAR TIER's table (CarHD): each game livery sheet that
    /// has an HD set, the set's key in the bundle, and each body's HD lamp
    /// map. Built by Editor/CarHDTool in the PC build's sandbox from
    /// PSX_CAR_HD_SRC; never committed (gitignored, like TextureHDMap).</summary>
    public class CarHDMap : ScriptableObject
    {
        public const string ResourcePath = "CarHDMap";

        /// <summary>One livery: <c>original</c> is the game's 512 x 256 sheet
        /// (the skin material's _MainTex); <c>key</c> = &lt;body&gt;__&lt;sheet&gt;, the
        /// bundle names <c>key</c> (colour), <c>normalKey</c> and <c>ormKey</c>
        /// (= key_normal / key_orm, or ANOTHER livery's when the files are
        /// byte-identical: two paints of one body share one normal and one ORM
        /// map in the bundle and in memory).</summary>
        [System.Serializable]
        public struct Sheet { public Texture2D original; public string body, key, normalKey, ormKey; }
        public Sheet[] sheets;

        /// <summary>A body's lamp control map at the HD sheet's size, bundle
        /// name <c>&lt;body&gt;_lightmask_hd</c>.</summary>
        [System.Serializable]
        public struct Lamp { public string body, key; }
        public Lamp[] lamps;
    }
}
