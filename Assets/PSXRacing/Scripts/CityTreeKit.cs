using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// CHARLOTTE'S MESH TREE KIT (2026-11-11): the species the city's mesh trees
    /// are drawn with, from the owner's packs - NatureManufacture Forest (beeches,
    /// small beeches), Meadow (broad poplars, grey willow), Objective Environment
    /// Big Oak and Big Poplar - each with the pack's own LODs and an impostor made
    /// from that model, and per species its crown as a volume (the radius of
    /// every vertex in each of <see cref="Bands"/> height bands) so a tree is
    /// fitted against what stands round it before it is drawn.
    ///
    /// One Resources asset written by the PC scene pass
    /// (PSXRacingBuilder.EnsureCityTreeKit, PSX_PC_CONTENT=1) and deleted by any
    /// other: the web and phone builds carry no store content and keep the cards.
    /// </summary>
    public class CityTreeKit : ScriptableObject
    {
        public const string ResourcePath = "CityTreeKit";
        public const int Bands = 12;

        /// <summary>What a species stands for (CityMeshTrees picks by it).</summary>
        public enum Role : byte
        {
            /// <summary>Big Oak: the old city's willow oaks, park and yard hero trees.</summary>
            Oak,
            /// <summary>The Forest beeches: woods, yards, mixed hardwood, creek sycamores.</summary>
            Broad,
            /// <summary>The Meadow poplars: tulip-tree role, and the street trees (clear stem, crown high).</summary>
            Poplar,
            /// <summary>The small beeches: young understory trees.</summary>
            Small,
            /// <summary>Big Poplar (columnar): sparingly.</summary>
            Columnar,
            /// <summary>The grey willow bushes: crape myrtle's multi-stem stand-in.</summary>
            Shrub,
            /// <summary>A Meadow poplar kept in leaf all year, darker: the loblolly pine's stand-in
            /// (no owned pine model).</summary>
            PineStandIn,
            /// <summary>A beech always bare: the dead snags.</summary>
            Snag,
        }

        public MeshForest.Variant[] variants;
        public string[] names;
        public Role[] roles;
        /// <summary>Per variant x <see cref="Bands"/>: the crown's radius (m, at
        /// scale 1) about the trunk in each height band of the model's own height.</summary>
        public float[] crown;
        /// <summary>Per variant: where the leaves begin, as a share of the height.</summary>
        public float[] crownBase;
        public ComputeShader cull;
        /// <summary>The MEDIUM level distances (ForestDetailPrefs and PopFix scale
        /// them as the stages').</summary>
        public float lod0To = 65f, lod1To = 140f, meshRange = 240f, farRange = 700f;

        static CityTreeKit cached;
        static bool tried;

        /// <summary>The kit, or null (no PC content in this build).</summary>
        public static CityTreeKit Get()
        {
            if (cached != null || tried) return cached;
            tried = true;
            cached = Resources.Load<CityTreeKit>(ResourcePath);
            return cached;
        }

        /// <summary>Forget the loaded kit (the editor rewrote it).</summary>
        public static void Reset() { cached = null; tried = false; }

        public float CrownR(int v, int band) => crown != null && v * Bands + band < crown.Length ? crown[v * Bands + band] : 0f;
    }
}
