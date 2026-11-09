using UnityEngine;
using UnityEngine.InputSystem;

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
    /// THE HORIZON (2026-11-09, the PC profile). The owner, of a dusk frame on
    /// NC 226A: "I still see the haze on the dusk horizon and haze on distant
    /// mountains". Measured (Editor/HazeProbe): the pale band WAS the distant
    /// mountain - the last 224 m before a 2.24 km far plane, painted in the
    /// end-of-view fade, which toward a low sun is the twilight's own pink -
    /// and behind it the world stopped, while the bake's DEM holds the real
    /// ridges for another 8 km. So a scene with a horizon (the builder's
    /// BuildStageHorizon: the real DEM to its edge, and an apron beyond) keeps
    /// TWO distances: everything with detail in it - road, props, the near
    /// ground, the forest - still ends at <see cref="pcFarClip"/>, and the
    /// land itself (the Horizon layer) is drawn to <see cref="horizonFarClip"/>
    /// with no fade in front of it.
    ///
    /// A component rather than builder-baked camera state, because the camera
    /// gets rebuilt by quality switches and the cull array has to survive that.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class StageCulling : MonoBehaviour
    {
        public const string HorizonLayerName = "Horizon";
        /// <summary>The detail dissolves over this last stretch before its edge (m).</summary>
        public const float DetailFadeM = 220f;
        static readonly int DetailFadeId = Shader.PropertyToID("_PSXDetailFade");

        public float foliageDistance = 520f;
        /// <summary>The PC profile's DETAIL reach: the far ground's own edge
        /// (the builder writes it). 0 keeps the scene's far plane.</summary>
        public float pcFarClip;
        /// <summary>The PC profile's far plane where the scene carries a
        /// horizon (the builder writes it): the apron's edge. 0 = none.</summary>
        public float horizonFarClip;

        /// <summary>The live per-layer distances, for the cameras that show
        /// the same world (the mirrors): null outside a stage.</summary>
        public static float[] LayerDistances { get; private set; }
        /// <summary>The scene's own far plane, as it loaded.</summary>
        float sceneFar;

        void OnEnable() => Configure(GetComponent<Camera>());

        /// <summary>F9: THE BLUE DISTANCE on and off (LookChoices.AerialDepth),
        /// the owner's A/B in the player - only where there is a horizon to
        /// tint. Nothing is saved: it is a look he has not chosen yet.</summary>
        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.f9Key.wasPressedThisFrame || TimeOfDay.HorizonM <= 0f) return;
            LookChoices.AerialForce = !LookChoices.AerialDepth;
            var sun = GameObject.Find("Sun");
            TimeOfDay.Apply(TimeOfDay.Current, sun != null ? sun.GetComponent<Light>() : null);
            Debug.Log("[StageCulling] blue distance " + (LookChoices.AerialDepth ? "ON" : "OFF"));
        }

        void OnDisable()
        {
            TimeOfDay.FarEdgeM = 0f;
            TimeOfDay.HorizonM = 0f;
            LayerDistances = null;
            Shader.SetGlobalVector(DetailFadeId, Vector4.zero);
        }

        /// <summary>Set the camera's planes and layer distances for this
        /// platform. Public for the edit-mode tools, where OnEnable never runs.</summary>
        public void Configure(Camera cam)
        {
            int foliage = LayerMask.NameToLayer("Foliage");
            if (cam == null || foliage < 0) return;
            bool full = PlatformProfile.FullForest;
            var d = new float[32];
            // THE WHOLE FOREST (the PC profile, a desktop browser -
            // PlatformProfile.FullForest): the forest to the detail's edge (0 =
            // the far plane), and the plane out to the far ground's edge (owner,
            // 2026-11-02: "trees still pop-in").
            d[foliage] = full ? 0f : foliageDistance;
            if (sceneFar <= 0f) sceneFar = cam.farClipPlane;
            float detail = full ? Mathf.Max(sceneFar, pcFarClip) : sceneFar;
            cam.farClipPlane = detail;
            int horizon = LayerMask.NameToLayer(HorizonLayerName);
            bool far = full && PlatformProfile.StageHorizon && horizon >= 0 && horizonFarClip > detail;
            if (far)
            {
                // Two distances: every layer ends where it always did, the
                // land alone runs on to the horizon.
                for (int i = 0; i < d.Length; i++) d[i] = detail;
                d[horizon] = 0f;
                cam.farClipPlane = horizonFarClip;
            }
            // the end-of-view fade belongs at the edge of what is drawn
            TimeOfDay.FarEdgeM = full ? detail : 0f;
            TimeOfDay.HorizonM = far ? horizonFarClip : 0f;
            // Nothing pops at the detail's edge: all but the land dissolves over
            // its last stretch (PSX/Lit _PSXDetailFade) - by distance from the
            // eye, so inside every layer's own (planar) limit whichever way the
            // camera faces.
            Shader.SetGlobalVector(DetailFadeId, far ? new Vector4(detail - DetailFadeM - 40f, detail - 40f, 0f, 0f) : Vector4.zero);
            cam.layerCullDistances = d;
            LayerDistances = d;
            // Spherical rather than planar: on a switchback the next arm of
            // the road is beside you, and planar distance would pop its trees
            // by camera yaw.
            cam.layerCullSpherical = true;
        }
    }
}
