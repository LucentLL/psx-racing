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
        /// <param name="missingWheels">Bitmask of wheels that have been
        /// STRIPPED off the car — bit i is wheel i in the loop's own order
        /// (FL, FR, RL, RR). The junkyard's whole read: a shelf selling used
        /// wheels next to a yard of cars all wearing four of them is a shop
        /// that lies about where its stock comes from.</param>
        /// <param name="blockMat">When a wheel is missing, a stack of cinder
        /// blocks goes under that corner so the car is supported rather than
        /// half buried — the material is the caller's, because a runtime class
        /// cannot conjure a PSX/Lit material that fits the scene.</param>
        /// <param name="widthMm">The real car's width (CarSpec.widthMm); 0 for
        /// the model's own reference car. The shell is scaled across to it
        /// (CarModelLibrary.WidthScale), wheels and box with it.</param>
        public static Transform Spawn(Transform at, CarModelDef def, int skin,
                                      out Vector3 roofPoint, bool solid = true,
                                      int missingWheels = 0, Material blockMat = null, int widthMm = 0,
                                      CarSpec spec = null, OwnedCar owned = null, bool standsHere = true)
        {
            roofPoint = new Vector3(0f, 1.1f, 0f);
            if (at == null || def == null) return null;
            // The same fit the car races on (CarModelLibrary.Fit): stretched
            // to the spec, the sheet's track and tyres, the camber.
            var fit = CarModelLibrary.Fit(def, spec != null ? CarModelLibrary.SpecGeometry.Of(spec)
                                                            : CarModelLibrary.SpecGeometry.WidthOnly(widthMm));
            float sx = fit.sx;

            var mat = def.SkinCount > 0
                ? def.skinMaterials[Mathf.Clamp(skin, 0, def.SkinCount - 1)] : null;
            var wheelMat = def.wheelMaterial != null ? def.wheelMaterial : mat;
            float centre = fit.P(def.colliderCenter).z;
            var slide = new Vector3(0f, 0f, -centre);

            var shellGO = new GameObject("Shell");
            shellGO.transform.SetParent(at, false);
            var shell = shellGO.transform;

            var body = new GameObject("Body");
            body.transform.SetParent(shell, false);
            fit.PlaceBody(body.transform, def, slide);
            body.AddComponent<MeshFilter>().sharedMesh = def.bodyMesh;
            var br = body.AddComponent<MeshRenderer>();
            if (mat != null) br.sharedMaterial = mat;

            // The player's own car wears whatever they fitted, or nothing.
            var donor = owned != null && !string.IsNullOrEmpty(owned.wheelKey) && owned.wheelKey != def.key
                ? CarModelLibrary.Load(owned.wheelKey) : null;
            if (donor != null && donor.wheelMesh == null) donor = null;
            var donorMat = CarModelLibrary.WheelMaterial(donor);
            if (donorMat != null) PSXTexDecode.Stamp(donorMat);
            var donorRatio = CarModelLibrary.WheelRatio(def, donor);

            for (int i = 0; i < 4; i++)
            {
                var hub = fit.Hub(i) + slide;

                if (owned != null && owned.noWheels)
                {
                    if (standsHere && mat != null) BuildJackStand(shell, "Stand" + i, def, fit, i, slide, mat);
                    continue;
                }

                if ((missingWheels & (1 << i)) != 0)
                {
                    // The wheel is on a shelf somewhere. What holds the corner
                    // up is a two-block stack under the sill, topping out at
                    // hub height so the body sits exactly where its wheels
                    // would have put it — a car on blocks is level; a car on a
                    // guess is a car with one corner in the dirt.
                    BuildBlockStack(shell, "Blocks" + i, hub, fit.Radius(i), blockMat);
                    continue;
                }

                var w = new GameObject("Wheel" + i);
                w.transform.SetParent(shell, false);
                fit.PlaceWheel(w.transform, i, slide);
                w.AddComponent<MeshFilter>().sharedMesh = donor != null ? donor.wheelMesh : def.wheelMesh;
                var wr = w.AddComponent<MeshRenderer>();
                if (donor != null)
                {
                    if (donorMat != null) wr.sharedMaterial = donorMat;
                    w.transform.localScale = Vector3.Scale(w.transform.localScale, donorRatio);
                }
                else if (wheelMat != null) wr.sharedMaterial = wheelMat;
            }

            if (solid)
            {
                var col = new GameObject("Solid");
                col.transform.SetParent(shell, false);
                col.transform.localPosition = new Vector3(0f, def.colliderCenter.y + fit.dy, 0f);
                var box = col.AddComponent<BoxCollider>();
                box.size = new Vector3(def.colliderSize.x * sx, def.colliderSize.y, def.colliderSize.z * fit.sz);
            }

            roofPoint = new Vector3(0f, Mathf.Max(def.roofY + fit.dy, 1.1f) * 0.82f, 0f);
            // A car, for SPEED BLUR: MOTION's mask: the lens moving never
            // smears one (traffic, the town's cars, the lots).
            SpeedBlur.MarkOtherCar(shell);
            return shell;
        }

        static Mesh standMesh;
        static Material standMaterial;

        /// <summary>A unit-high jack stand: a four-sided taper with a cradle
        /// width at the top, for scaling to the sill it holds up.</summary>
        public static Mesh StandMesh()
        {
            if (standMesh != null) return standMesh;
            var v = new System.Collections.Generic.List<Vector3>();
            var t = new System.Collections.Generic.List<int>();
            void Box(float half, float y0, float y1)
            {
                Vector3 P(float x, float y, float z) => new Vector3(x * half, y, z * half);
                void Side(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
                {
                    int n = v.Count;
                    v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
                    t.AddRange(new[] { n, n + 2, n + 1, n, n + 3, n + 2 });
                }
                Side(P(-1, y0, -1), P(1, y0, -1), P(1, y1, -1), P(-1, y1, -1));
                Side(P(1, y0, -1), P(1, y0, 1), P(1, y1, 1), P(1, y1, -1));
                Side(P(1, y0, 1), P(-1, y0, 1), P(-1, y1, 1), P(1, y1, 1));
                Side(P(-1, y0, 1), P(-1, y0, -1), P(-1, y1, -1), P(-1, y1, 1));
                Side(P(-1, y1, -1), P(1, y1, -1), P(1, y1, 1), P(-1, y1, 1));
            }
            // A foot, a post and a saddle, as the garage bay's stands are.
            Box(0.13f, 0f, 0.08f);
            Box(0.06f, 0.08f, 0.86f);
            Box(0.09f, 0.86f, 1f);
            standMesh = new Mesh { name = "JackStand" };
            standMesh.SetVertices(v);
            var uv = new Vector2[v.Count];
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(0.25f, 0.5f);
            standMesh.uv = uv;
            standMesh.SetTriangles(t, 0);
            standMesh.RecalculateNormals();
            standMesh.RecalculateBounds();
            return standMesh;
        }

        /// <summary>The stand's plain steel, made from a material of the shell's
        /// so no shader has to be found by name in a stripped build.</summary>
        public static Material StandMaterial(Material from)
        {
            if (standMaterial != null) return standMaterial;
            standMaterial = new Material(from);
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels32(new[] { new Color32(165, 38, 30, 255) });
            tex.Apply(false);
            standMaterial.mainTexture = tex;
            PSXTexDecode.Stamp(standMaterial);
            return standMaterial;
        }

        /// <summary>Where stand i goes, before the shell's slide: under the sill,
        /// inboard of the wheels, up to the shell's own measured floor. The same
        /// answer as the bay's rig (GarageWorld.BuildRaiseRig), because no one height
        /// off the tyre reaches both a Land Rover and an NSX, and at the axle there
        /// is only the empty wheel well to hold up.</summary>
        public static void StandPlace(CarModelDef def, CarModelLibrary.ShellFit fit, int i,
                                      out Vector3 at, out Vector3 scale)
        {
            float x = Mathf.Max(0.52f, Mathf.Min(fit.trackF, fit.trackR) * 0.42f);
            float z = Mathf.Max(0.62f, fit.wheelbase * 0.30f);
            float floor = def.colliderCenter.y + fit.dy - def.colliderSize.y * 0.5f;
            at = new Vector3((i % 2 == 0 ? -1f : 1f) * x, 0f, (i < 2 ? 1f : -1f) * z);
            // Into the floor a little, so no angle shows a gap; a short one is narrower.
            float high = Mathf.Max(0.10f, floor + 0.03f);
            float wide = Mathf.Clamp(high / 0.3f, 0.65f, 1.15f);
            scale = new Vector3(wide, high, wide);
        }

        static void BuildJackStand(Transform shell, string name, CarModelDef def, CarModelLibrary.ShellFit fit,
                                   int i, Vector3 slide, Material from)
        {
            var go = new GameObject(name);
            go.transform.SetParent(shell, false);
            StandPlace(def, fit, i, out Vector3 at, out Vector3 scale);
            go.transform.localPosition = at + slide;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = StandMesh();
            go.AddComponent<MeshRenderer>().sharedMaterial = StandMaterial(from);
        }

        /// <summary>
        /// Two cinder blocks, laid crosswise, from the ground up to hub
        /// height. Primitive cubes because there is no block model in either
        /// art tree — the same reason the garage's jack stands are primitives —
        /// and crosswise because that is how anyone who has actually done this
        /// stacks them.
        /// </summary>
        static void BuildBlockStack(Transform shell, string name, Vector3 hub,
                                    float topY, Material mat)
        {
            float each = Mathf.Max(0.09f, topY * 0.5f);
            for (int layer = 0; layer < 2; layer++)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = name + "_" + layer;
                // The preview tools run this in edit mode, where Destroy is an
                // error rather than a deferral.
                var col = b.GetComponent<Collider>();
                if (Application.isPlaying) Object.Destroy(col);
                else Object.DestroyImmediate(col);
                b.transform.SetParent(shell, false);
                b.transform.localPosition = new Vector3(
                    hub.x, each * (layer + 0.5f), hub.z);
                // Crosswise: the long axis alternates per layer.
                b.transform.localRotation = Quaternion.Euler(0f, layer * 90f, 0f);
                b.transform.localScale = new Vector3(0.44f, each, 0.22f);
                var r = b.GetComponent<MeshRenderer>();
                if (mat != null) r.sharedMaterial = mat;
            }
        }
    }
}
