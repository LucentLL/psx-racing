using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Puts a <see cref="CarModelDef"/> on a car: swaps the body and wheel
    /// meshes, picks a livery, and re-fits the parts of the rig that are the
    /// car's SHAPE rather than its tune - collider, blob shadow, wheelbase,
    /// track and tyre radius.
    ///
    /// The builder wires the references once and calls this at bake time, so a
    /// scene opened in the editor already shows the right cars with no runtime
    /// work. RaceHandoffApplier calls it again when the LifeSim hands over a
    /// grid, BEFORE ApplySpec: gearing is anchored to wheel radius, so the
    /// wheels have to be the new car's before the gearbox is built off them.
    ///
    /// Geometry is applied through <see cref="CarController.RebuildGeometry"/>
    /// rather than by writing the fields and hoping. Those fields are read once
    /// in Awake into a suspension-mount table; setting wheelbase afterwards and
    /// not rebuilding leaves a Charger driving on an RX-7's footprint.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class CarBody : MonoBehaviour
    {
        [Header("Wired by the builder")]
        public CarController car;
        public BoxCollider box;
        public Transform bodyRoot;
        public MeshFilter bodyFilter;
        public MeshRenderer bodyRenderer;
        /// <summary>Per wheel: the holder that carries the model scale and the
        /// left/right flip. Child of the steering hub.</summary>
        public Transform[] wheelHolders = new Transform[4];
        /// <summary>Per wheel: the spinning mesh under the holder.</summary>
        public MeshFilter[] wheelFilters = new MeshFilter[4];
        public MeshRenderer[] wheelRenderers = new MeshRenderer[4];
        public Transform blobShadow;

        [Header("State")]
        public string modelKey = CarModelLibrary.Default;
        public int skinIndex;
        /// <summary>The real width this car is scaled across to, mm
        /// (CarSpec.widthMm); 0 = the model's reference car. Kept, so a
        /// respray (Apply with the same shell) keeps the car's width.</summary>
        public int widthMm;
        /// <summary>The across-scale last applied (CarModelLibrary.WidthScale).</summary>
        public float WidthScale { get; private set; } = 1f;

        /// <summary>A body transform's local scale that makes the car
        /// <paramref name="sx"/> times as wide - across the CAR, whichever of
        /// the body's own axes that is after its yaw.</summary>
        public static Vector3 AcrossScale(float bodyYaw, float sx)
        {
            float s = Mathf.Abs(Mathf.Sin(bodyYaw * Mathf.Deg2Rad));
            return s > 0.7f ? new Vector3(1f, 1f, sx) : new Vector3(sx, 1f, 1f);
        }

        CarModelDef cachedDef;
        /// <summary>
        /// The shell currently fitted. Anything needing a MEASUREMENT off this
        /// car — the bonnet camera wants the base of the windscreen — asks the
        /// model rather than guessing from the collider, which only knows the
        /// bounding box.
        ///
        /// Resolved from <see cref="modelKey"/> when it has not been set
        /// directly. Apply() runs at BAKE time for a scene's own grid, and a
        /// plain object reference would not survive the save — so on a
        /// standalone editor race the reference would be null and every
        /// measurement would silently fall back. The key does survive.
        /// </summary>
        public CarModelDef Def =>
            cachedDef != null ? cachedDef : cachedDef = CarModelLibrary.Load(modelKey);

        /// <summary>
        /// Fit the chassis to the shell as well as re-skinning it. On by
        /// default: a 1969 Charger is 60 cm longer in the wheelbase than the FD
        /// this game was tuned around, and having it turn in like an FD is a
        /// bigger lie than the mesh swap fixes. Turn it off to get the old
        /// behaviour - every car on the FD's exact footprint - if a handling
        /// change ever needs to be isolated from the body.
        /// </summary>
        public bool applyGeometry = true;

        /// <summary>Give this car the shell its catalog entry deserves.</summary>
        public void ApplySpec(CarSpec spec)
        {
            if (spec == null) return;
            var def = CarModelLibrary.LoadFor(spec);
            if (def == null) return;
            // Salt the livery with the id so two identical opponents are not
            // guaranteed to be the same colour when the catalog colour is
            // missing or when several liveries tie.
            widthMm = spec.widthMm;
            Apply(def, def.SkinFor(spec.color, Mathf.Abs(spec.id != null ? spec.id.GetHashCode() : 0) % 97));
        }

        public void ApplyKey(string key, int skin)
        {
            widthMm = 0;
            Apply(CarModelLibrary.Load(key), skin);
        }

        public void Apply(CarModelDef def, int skin)
        {
            if (def == null) return;
            modelKey = def.key;
            cachedDef = def;
            // With the chassis left alone (applyGeometry off) the wheels stay on
            // the model's track, so the body keeps its width too.
            float sx = applyGeometry ? CarModelLibrary.WidthScale(def, widthMm) : 1f;
            WidthScale = sx;

            var mat = def.SkinCount > 0
                ? def.skinMaterials[Mathf.Clamp(skin, 0, def.SkinCount - 1)]
                : null;
            skinIndex = def.SkinCount > 0 ? Mathf.Clamp(skin, 0, def.SkinCount - 1) : -1;

            if (bodyFilter != null) bodyFilter.sharedMesh = def.bodyMesh;
            PSXTexDecode.Stamp(mat);   // a livery's 16-bit sheet is decoded in the shader
            if (bodyRenderer != null && mat != null) bodyRenderer.sharedMaterial = mat;
            if (bodyRoot != null)
            {
                bodyRoot.localRotation = Quaternion.Euler(0f, def.bodyYaw, 0f);
                // Z as well as Y. The rig's wheels are symmetric about its own
                // origin and the pack's models are not symmetric about theirs —
                // leaving this at zero is what put a GTO's wheels a quarter of a
                // metre behind its arches.
                bodyRoot.localPosition = new Vector3(0f, def.bodyYOffset, def.bodyZOffset);
                // As wide as the real car (the lamps hang off this root and
                // move with it).
                bodyRoot.localScale = AcrossScale(def.bodyYaw, sx);
            }

            // Wheels ride on the body's livery: the pack draws them on a neutral
            // patch of the same sheet, so they stay grey while the paint changes.
            // The FD is the exception and carries its own wheel material.
            var wheelMat = def.wheelMaterial != null ? def.wheelMaterial : mat;
            PSXTexDecode.Stamp(wheelMat);
            for (int i = 0; i < 4; i++)
            {
                if (wheelFilters[i] != null) wheelFilters[i].sharedMesh = def.wheelMesh;
                if (wheelRenderers[i] != null && wheelMat != null) wheelRenderers[i].sharedMaterial = wheelMat;
                CarPaint.DullWheels(wheelRenderers[i]);
                if (wheelHolders[i] != null)
                    wheelHolders[i].localScale = Vector3.one * def.wheelMeshScale;
            }

            if (blobShadow != null)
            {
                blobShadow.localScale = new Vector3(def.blobSize.x * sx, def.blobSize.y, 1f);
                // Under the BODY, which is no longer over the rig's origin.
                var bs = blobShadow.localPosition;
                blobShadow.localPosition = new Vector3(0f, bs.y, def.bodyZOffset);
            }

            if (!applyGeometry || car == null) return;

            if (box != null)
            {
                box.center = def.colliderCenter;
                box.size = new Vector3(def.colliderSize.x * sx, def.colliderSize.y, def.colliderSize.z);
            }
            car.wheelbase = def.wheelbase;
            // The wheels stay in the arches of the narrowed body.
            car.trackWidth = def.trackWidth * sx;
            car.wheelRadius = def.wheelRadius;
            car.RebuildGeometry();
        }
    }
}
