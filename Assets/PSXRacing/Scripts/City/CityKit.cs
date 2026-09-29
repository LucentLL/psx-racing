using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// THE CITY KIT (Charlotte refinement WP-07): every material the streamed
    /// city draws with, in ONE Resources asset that CityWorld loads itself.
    ///
    /// The four city scenes used to serialize the materials array on their
    /// CityWorld (one per <see cref="CityMeshes.Slot"/>, in enum order). That
    /// made every new slot or material a rebake of all four scenes, and a
    /// stale scene drew roads with facades - which is why the lamp posts, the
    /// one material added since, lived as an untextured tint made at runtime.
    /// Now a scene holds no city material at all: the builder rewrites this
    /// asset (PSXRacingBuilder.EnsureCityKit) and every scene reads it, so a
    /// later package adds a material by adding it HERE, with no city rebake.
    ///
    /// Holding them from Resources also keeps every city shader in the build:
    /// WebGL strips a shader nothing references, and a material a tile makes
    /// at runtime references nothing.
    ///
    /// The empty fields are the later packages' places, named now so they
    /// need no new plumbing: the canopy trees (WP-08, one per season dress),
    /// the roadside furniture atlas (poles, WP-15), the markings (WP-17) and
    /// the sign faces (WP-23).
    /// </summary>
    public class CityKit : ScriptableObject
    {
        /// <summary>Resources path of the one kit.</summary>
        public const string ResourcePath = "CityKit";

        [Tooltip("One material per CityMeshes.Slot, in enum order.")]
        public Material[] slots;
        [Tooltip("The street-lamp posts, arms and heads: pack metal.")]
        public Material lampPost;

        [Header("Reserved for later packages")]
        [Tooltip("WP-08 canopy trees, one per season dress (Seasons.DressCount).")]
        public Material[] trees;
        [Tooltip("WP-08: per atlas cell, how far the painted tree reaches out from its trunk below each twentieth of its height (cells x 21 levels, fraction of the card width; the widest of the five dresses).")]
        public float[] treeLowReach;
        [Tooltip("WP-15 roadside furniture atlas (poles, arms, cobra-heads).")]
        public Material furniture;
        [Tooltip("WP-17 markings.")]
        public Material paint;
        [Tooltip("WP-23 sign faces.")]
        public Material signs;

        [Tooltip("Every shader the city draws with, so no build strips one.")]
        public Shader[] shaders;

        /// <summary>The graph's slot count when this kit was written, so a
        /// kit baked before a Slot change says so instead of misdrawing.</summary>
        public int slotCount;

        static CityKit cached;
        static bool warned;

        /// <summary>The kit, loaded once. Null (and said once) when the build
        /// has none - the tiles then stand up with their renderers off.</summary>
        public static CityKit Get()
        {
            if (cached != null) return cached;
            cached = Resources.Load<CityKit>(ResourcePath);
            if (cached == null && !warned)
            {
                warned = true;
                Debug.LogError("[City] Resources/" + ResourcePath + " is missing - run the scene build (PSXRacingBuilder.EnsureCityKit).");
            }
            else if (cached != null && cached.slotCount != (int)CityMeshes.Slot.COUNT && !warned)
            {
                warned = true;
                Debug.LogError($"[City] the city kit holds {cached.slotCount} slots and the code has {(int)CityMeshes.Slot.COUNT} - rebuild it (PSXRacingBuilder.EnsureCityKit).");
            }
            return cached;
        }

        /// <summary>The material for a slot, or null.</summary>
        public Material MaterialFor(CityMeshes.Slot slot)
        {
            int i = (int)slot;
            return slots != null && i >= 0 && i < slots.Length ? slots[i] : null;
        }

#if UNITY_EDITOR
        /// <summary>The builder rewrote the asset: read it again.</summary>
        public static void Forget() { cached = null; warned = false; }
#endif
    }
}
