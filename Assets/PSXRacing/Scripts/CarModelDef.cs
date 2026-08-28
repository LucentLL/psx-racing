using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// One drivable body shell: the meshes, the liveries, and the handful of
    /// measurements the chassis needs to wear it.
    ///
    /// Everything here is MEASURED at bake time from the imported OBJ (see
    /// CarModelBaker) rather than typed in. The pack ships its models in metres
    /// with the wheels as named sub-objects, so the axles, the track, the tyre
    /// radius and the body box are all readable off the mesh - and reading them
    /// is the only way to be sure which way round Unity's OBJ importer put the
    /// car. A hand-written table would be four numbers per model that quietly
    /// disagree with the geometry the moment the pack updates.
    ///
    /// One prefab per model lives under Resources/CarModels, so a race loads the
    /// two or three shells its grid actually needs rather than all sixteen.
    /// </summary>
    public class CarModelDef : MonoBehaviour
    {
        [Tooltip("Library key, e.g. skyline_r32.")]
        public string key;
        [Tooltip("Human-readable, for the mapping report and debugging.")]
        public string displayName;

        public Mesh bodyMesh;
        /// <summary>One wheel, re-centred on its own axle so the car can spin
        /// it. Taken from the model's right-hand wheel: the rig mirrors it onto
        /// the left with a 180 degree yaw, the same as the built-in FD.</summary>
        public Mesh wheelMesh;

        /// <summary>One shared material per livery. Baked as assets rather than
        /// instanced at runtime: two opponents in the same shell and the same
        /// colour should be one draw material, and WebGL should not be building
        /// materials on the frame a race starts.</summary>
        public Material[] skinMaterials;
        public string[] skinNames;
        /// <summary>Overrides the body livery on the wheels. Only the built-in
        /// FD needs one: its wheel UVs sit ON the painted part of the sheet, so
        /// a red car would otherwise get red wheels. Every model in the pack
        /// paints its wheels on a neutral patch and leaves this null.</summary>
        public Material wheelMaterial;
        /// <summary>Mean colour of each livery, baked so a car can be given the
        /// one nearest the colour its catalog entry claims.</summary>
        public Color[] skinColors;

        /// <summary>Yaw that turns the model to face +Z.</summary>
        public float bodyYaw;
        /// <summary>Lift that puts the model's own wheel CENTRE on the height
        /// the rig hangs its hubs at, which is wheelRadius - i.e. the tyre
        /// scale is folded in here too. Putting the model's contact patch on
        /// y = 0 instead looks right until you notice the hub sitting a
        /// centimetre or two below the middle of the arch, because the drawn
        /// tyre is 7% smaller than the one the arch was modelled around.</summary>
        public float bodyYOffset;

        /// <summary>
        /// Slide along the car that puts the model's own axle midpoint on the
        /// rig's origin.
        ///
        /// The rig hangs its four wheels symmetrically about that origin, at
        /// +/- wheelbase/2. Almost nothing in the pack is modelled that way:
        /// a '66 GTO's axles straddle a point 25 cm ahead of its mesh origin,
        /// a '69 Charger's 20 cm, and with the body pinned at the origin every
        /// one of those centimetres is a wheel sitting behind its own arch.
        /// MEASURED from the two axle OBJs, so it costs nothing to be right.
        /// </summary>
        public float bodyZOffset;

        public float wheelbase = 2.425f;
        public float trackWidth = 1.46f;
        public float wheelRadius = 0.31f;
        /// <summary>Scale on the wheel mesh. The project has always run the FD's
        /// tyre at 0.93 so it sits in the arch rather than proud of it; the same
        /// factor is folded into wheelRadius above, so the visible tyre is the
        /// one the physics is using.</summary>
        public float wheelMeshScale = 0.93f;

        /// <summary>
        /// Base of the windscreen, in the same car-local frame as the collider:
        /// Z along the car with +Z out of the nose, Y with the tyre contact
        /// patch at zero. MEASURED off the body mesh by CarModelBaker.
        ///
        /// This exists for the bonnet camera. Placing that camera at a fixed
        /// FRACTION of the car's length is wrong in the way that matters most —
        /// a Superbird's bonnet is nearly two metres long and a 1970s Civic's is
        /// barely half of that, so one fraction puts the lens inside the cabin
        /// on one car and out over the front bumper on the other.
        /// </summary>
        public float cowlZ;
        /// <summary>
        /// Height of the bodywork at <see cref="cowlZ"/> — or anywhere ahead of
        /// it, whichever is higher. The bonnet camera has to clear the whole
        /// bonnet, not just the slice it stands on, and a scoop or a raised
        /// centre crest would otherwise be modelled as thin air.
        /// </summary>
        public float cowlY = 0.9f;

        /// <summary>Front-most point of the bodywork, in the same frame as
        /// <see cref="cowlZ"/>. The bumper camera is placed from this rather
        /// than from the collider: the box is a SHRUNK fit, so its front face
        /// sits a good 10 cm inside the nose of the mesh and a camera set just
        /// ahead of the box ends up inside the car's own bodywork.</summary>
        public float noseZ = 2.15f;
        /// <summary>Top of the bodywork, measured. The collider box is a
        /// SHRUNK fit (ColliderFit), so deriving a roofline from it puts a roof
        /// camera inside the cabin on anything tall.</summary>
        public float roofY = 1.25f;

        public Vector3 colliderCenter = new Vector3(0f, 0.72f, 0.05f);
        public Vector3 colliderSize = new Vector3(1.72f, 1.0f, 4.1f);
        /// <summary>Width/length of the blob shadow quad.</summary>
        public Vector2 blobSize = new Vector2(2.3f, 4.6f);

        public int SkinCount => skinMaterials != null ? skinMaterials.Length : 0;

        /// <summary>
        /// Pick the livery closest to a catalog colour, falling back to a
        /// salted index when the entry has no usable colour.
        /// </summary>
        public int SkinFor(string hexColor, int salt)
        {
            int n = SkinCount;
            if (n == 0) return -1;
            if (skinColors == null || skinColors.Length != n ||
                string.IsNullOrEmpty(hexColor) ||
                !ColorUtility.TryParseHtmlString(hexColor, out var want))
                return (salt % n + n) % n;

            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var c = skinColors[i];
                // Weighted RGB: the eye separates liveries mostly on hue, and
                // plain Euclidean RGB keeps handing a dark red car a dark green
                // one on the grounds that both are simply dark.
                float dr = (c.r - want.r) * 1.2f, dg = (c.g - want.g) * 1.4f, db = (c.b - want.b) * 0.9f;
                float d = dr * dr + dg * dg + db * db;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }
    }
}
