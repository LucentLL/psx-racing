using UnityEngine;
using PSXRacing.LifeSim;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// A car you can walk around but not drive: body, four wheels, and one box
    /// to bump into.
    ///
    /// Lifted out of <see cref="GarageWorld"/> the moment a second place wanted
    /// one. There are now three — the garage bays, the dealership lot and the
    /// junkyard in town, and a seller's driveway — and the geometry is measured
    /// off the mesh at bake time, so a second copy of these five lines would be
    /// a second opinion about where a Charger's wheels go.
    ///
    /// Deliberately NOT the drivable car. That is
    /// <c>PSXRacingBuilder.BuildOneCar</c>, which brings a rigidbody, a
    /// suspension model, audio and an input stack; a forecourt with eight of
    /// those in it is eight physics bodies settling on their springs while the
    /// player reads a price sticker.
    /// </summary>
    public static class CarShell
    {
        /// <summary>
        /// The model a car should wear.
        ///
        /// Falls back to the default shell when the resolver has nothing for
        /// this spec, rather than returning null. The garage can afford null —
        /// an unmodelled car leaves an empty bay and the player still has four
        /// others — but a SELLER'S DRIVEWAY cannot: the car is the only thing
        /// in the scene, the prompt hangs off it, and a null there is a house
        /// with nothing outside it and no way to do the deal.
        /// </summary>
        public static CarModelDef DefFor(CarSpec spec)
        {
            var def = spec != null ? CarModelLibrary.LoadFor(spec) : null;
            return def != null ? def : CarModelLibrary.Load(CarModelLibrary.Default);
        }

        /// <summary>Which paint. Seeded per instance so two of the same model
        /// on one lot are not the same colour.</summary>
        public static int SkinFor(CarModelDef def, CarSpec spec, int seed) =>
            def == null ? 0
            : spec != null ? def.SkinFor(spec.color, Mathf.Abs(seed) % 97)
            : 0;

        /// <summary>
        /// Park a shell under <paramref name="at"/>. The anchor marks where the
        /// car's MIDDLE goes, not its nose — the same convention the garage
        /// bays and the menu turntable use, so a long car and a short one both
        /// centre in their space.
        /// </summary>
        /// <param name="roofPoint">Local aim point for a FootTarget: the roof
        /// line, not the origin. A car's transform sits between its axles at
        /// road height, and a hook that aims there is one the ground stands in
        /// front of.</param>
        public static Transform Spawn(Transform at, CarModelDef def, int skin,
                                      out Vector3 roofPoint, bool solid = true)
        {
            roofPoint = new Vector3(0f, 1.1f, 0f);
            if (at == null || def == null) return null;

            var mat = def.SkinCount > 0
                ? def.skinMaterials[Mathf.Clamp(skin, 0, def.SkinCount - 1)] : null;
            var wheelMat = def.wheelMaterial != null ? def.wheelMaterial : mat;
            float centre = def.colliderCenter.z;

            var shellGO = new GameObject("Shell");
            shellGO.transform.SetParent(at, false);
            var shell = shellGO.transform;

            var body = new GameObject("Body");
            body.transform.SetParent(shell, false);
            body.transform.localPosition = new Vector3(0f, def.bodyYOffset, def.bodyZOffset - centre);
            body.transform.localRotation = Quaternion.Euler(0f, def.bodyYaw, 0f);
            body.AddComponent<MeshFilter>().sharedMesh = def.bodyMesh;
            var br = body.AddComponent<MeshRenderer>();
            if (mat != null) br.sharedMaterial = mat;

            for (int i = 0; i < 4; i++)
            {
                bool left = i % 2 == 0;
                var w = new GameObject("Wheel" + i);
                w.transform.SetParent(shell, false);
                w.transform.localPosition = new Vector3(
                    (left ? -0.5f : 0.5f) * def.trackWidth,
                    def.wheelRadius,
                    (i < 2 ? 0.5f : -0.5f) * def.wheelbase - centre);
                w.transform.localRotation = Quaternion.Euler(0f, left ? 180f : 0f, 0f);
                w.transform.localScale = Vector3.one * def.wheelMeshScale;
                w.AddComponent<MeshFilter>().sharedMesh = def.wheelMesh;
                var wr = w.AddComponent<MeshRenderer>();
                if (wheelMat != null) wr.sharedMaterial = wheelMat;
            }

            if (solid)
            {
                var col = new GameObject("Solid");
                col.transform.SetParent(shell, false);
                col.transform.localPosition = new Vector3(0f, def.colliderCenter.y, 0f);
                var box = col.AddComponent<BoxCollider>();
                box.size = def.colliderSize;
            }

            roofPoint = new Vector3(0f, Mathf.Max(def.roofY, 1.1f) * 0.82f, 0f);
            return shell;
        }
    }
}
