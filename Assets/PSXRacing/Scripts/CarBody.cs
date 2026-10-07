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
        /// <summary>The car's spec-sheet geometry (wheelbase, track, tyres),
        /// kept for the same reason as <see cref="widthMm"/>. Empty for a car
        /// with no catalog row.</summary>
        public CarModelLibrary.SpecGeometry specGeometry;

        /// <summary>
        /// THE FIT this car wears (CarModelLibrary.Fit): body stretch, chassis
        /// to spec, each tyre's size and camber. Re-solved from the key and the
        /// kept spec when read before Apply has run in this session (a baked
        /// scene keeps the key and the spec, not the struct).
        /// </summary>
        public CarModelLibrary.ShellFit Fit
        {
            get
            {
                if (!fitSolved) { fit = SolveFit(Def); fitSolved = true; }
                return fit;
            }
        }
        CarModelLibrary.ShellFit fit;
        bool fitSolved;

        /// <summary>The across-scale last applied (CarModelLibrary.WidthScale).</summary>
        public float WidthScale => applyGeometry ? Fit.sx : 1f;
        /// <summary>The along-scale: the shell's axles onto the spec wheelbase.</summary>
        public float LengthScale => applyGeometry ? Fit.sz : 1f;
        /// <summary>How far the body sits above its baked height (the tyres
        /// grew or shrank to spec); every baked Y measurement moves with it.</summary>
        public float BodyLift => applyGeometry ? Fit.dy : 0f;

        CarModelLibrary.ShellFit SolveFit(CarModelDef def)
        {
            // Someone set widthMm alone (a tool fitting a shell at its own
            // reference width): honour it and drop the rest of the sheet.
            var g = specGeometry.widthMm == widthMm ? specGeometry
                  : CarModelLibrary.SpecGeometry.WidthOnly(widthMm);
            return CarModelLibrary.Fit(def, g);
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
            specGeometry = CarModelLibrary.SpecGeometry.Of(spec);
            Apply(def, def.SkinFor(spec.color, Mathf.Abs(spec.id != null ? spec.id.GetHashCode() : 0) % 97));
        }

        public void ApplyKey(string key, int skin)
        {
            widthMm = 0;
            specGeometry = default;
            Apply(CarModelLibrary.Load(key), skin);
        }

        public void Apply(CarModelDef def, int skin)
        {
            if (def == null) return;
            modelKey = def.key;
            cachedDef = def;
            // ONE fit (CarModelLibrary.Fit) for the body, the wheels and the
            // chassis - the same one the garage, the props and the previews
            // draw. With the chassis left alone (applyGeometry off) the wheels
            // stay on the model's track, so the body keeps its shape too.
            fit = SolveFit(def);
            fitSolved = true;
            var f = applyGeometry ? fit : CarModelLibrary.Fit(def, default(CarModelLibrary.SpecGeometry));
            if (!applyGeometry) { f.sx = 1f; f.sz = 1f; f.bodyY = def.bodyYOffset; f.bodyZ = def.bodyZOffset; f.dy = 0f; }

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
                // Slid with the stretch, so the axle midpoint stays on the origin;
                // lifted with the tyres, so the hubs stay in the arches.
                bodyRoot.localPosition = new Vector3(0f, f.bodyY, f.bodyZ);
                // As wide and as long as the real car (the lamps hang off this
                // root and move with it).
                bodyRoot.localScale = f.BodyScale(def.bodyYaw);
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
                // Each tyre at the sheet's size, cambered in where it would
                // stand out past the arch.
                f.PlaceHolder(wheelHolders[i], i);
            }

            if (blobShadow != null)
            {
                blobShadow.localScale = new Vector3(def.blobSize.x * f.sx, def.blobSize.y * f.sz, 1f);
                // Under the BODY, which is no longer over the rig's origin.
                var bs = blobShadow.localPosition;
                blobShadow.localPosition = new Vector3(0f, bs.y, f.bodyZ);
            }

            if (!applyGeometry || car == null) return;

            if (box != null)
            {
                box.center = f.P(def.colliderCenter);
                box.size = new Vector3(def.colliderSize.x * f.sx, def.colliderSize.y, def.colliderSize.z * f.sz);
            }
            // TO SPEC: the sheet's wheelbase, each axle's track and tyre; the
            // gearbox turns through the DRIVEN axle's tyre.
            car.wheelbase = f.wheelbase;
            car.trackWidth = f.trackF;
            car.trackWidthRear = f.trackR;
            car.wheelRadiusFront = f.radiusF;
            car.wheelRadiusRear = f.radiusR;
            car.wheelRadius = f.driveRadius;
            car.RebuildGeometry();
        }
    }
}
