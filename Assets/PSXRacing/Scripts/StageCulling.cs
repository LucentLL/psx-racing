using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Per-layer draw distances for the mountain stage.
    ///
    /// The stage sees 3x further than a circuit so the ridgelines exist, but
    /// ten thousand tree billboards do not deserve that far plane: past ~500 m
    /// a 10 m tree is under two pixels of a 240-line frame and the far slopes
    /// are already painted as forest. The forest chunks live on their own
    /// layer and this clips that layer early, which is most of what keeps the
    /// stage at circuit framerates.
    ///
    /// A component rather than builder-baked camera state, because the camera
    /// gets rebuilt by quality switches and the cull array has to survive that.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class StageCulling : MonoBehaviour
    {
        public float foliageDistance = 520f;
        /// <summary>The PC profile's far plane: the far ground's reach (the
        /// builder writes it). 0 keeps the scene's.</summary>
        public float pcFarClip;

        void OnEnable()
        {
            var cam = GetComponent<Camera>();
            int layer = LayerMask.NameToLayer("Foliage");
            if (cam == null || layer < 0) return;
            var d = new float[32];
            // THE WHOLE FOREST (the PC profile, a desktop browser -
            // PlatformProfile.FullForest): the forest to the far plane (0 = the
            // plane), and the plane out to the far ground's edge (owner,
            // 2026-11-02: "trees still pop-in").
            d[layer] = PlatformProfile.FullForest ? 0f : foliageDistance;
            if (PlatformProfile.FullForest && pcFarClip > cam.farClipPlane) cam.farClipPlane = pcFarClip;
            cam.layerCullDistances = d;
            // Spherical rather than planar: on a switchback the next arm of
            // the road is beside you, and planar distance would pop its trees
            // by camera yaw.
            cam.layerCullSpherical = true;
        }
    }
}
