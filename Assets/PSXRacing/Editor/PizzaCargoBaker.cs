using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The cargo, as runtime-loadable prefabs: a pizza box, its lid, ten
    /// toppings and a ring of loose slices, cut out of the owner's own props
    /// pack and saved under Resources/PizzaCargo.
    ///
    /// Prefabs rather than direct FBX references for the reason CityProps
    /// already learned: the thing that stands these up is a RACE scene, built
    /// months apart from this pack and unable to AssetDatabase-load anything.
    /// Whatever the cargo is going to be, it has to already be a prefab with
    /// its PSX materials and its collision on it.
    ///
    /// Everything here is MEASURED. The pack is a showcase scene, so its parts
    /// carry whatever local transform put them on a shelf, and the only
    /// question that matters — which way is flat — is answered by looking at
    /// the bounds rather than by trusting an axis. A box baked on its edge is
    /// exactly the bug this pass exists to fix.
    /// </summary>
    public static class PizzaCargoBaker
    {
        const string Root = "Assets/PSXRacing";
        const string PackFbx = Root + "/Art/LifeSim/PizzeriaScene/Pizzeria_Props.fbx";
        public const string ResDir = Root + "/Resources/PizzaCargo";
        public const string ResPath = "PizzaCargo/";

        /// <summary>
        /// How wide a large pizza box really is, in metres.
        ///
        /// The pack's box measures 0.70 m across, which is a metre-square
        /// coffee table rather than a pizza box — this pack is the 1.23x
        /// oversized family CityProps.PackScale exists for, and even corrected
        /// it would be 0.57. A 41 cm box is a real 16-inch one, and the number
        /// has to be right because three of them have to stack on a car seat
        /// that is 50 cm wide. Derived as a SCALE from the measured mesh, so a
        /// pack update cannot silently resize the cargo.
        /// </summary>
        public const float BoxWidthM = 0.41f;

        /// <summary>The ten whole pizzas the pack ships, by the name they carry
        /// in it. Order is the order a topping id indexes into, so it is
        /// append-only — a saved order names its toppings by INDEX.</summary>
        public static readonly string[] Toppings =
        {
            "Pizza_Peperoni", "Cheese_Pizza", "Ham_Pizza", "Mushroom_pizza",
            "Olive_Pizza", "Pepper_Pizza", "Basil_Pizza", "Pineapple_Pizza",
            "Pizza_M", "Pizza_S",
        };

        /// <summary>Loose slices — the pack has a whole pizza already cut into
        /// ten wedges, which is exactly what a box that has been through a
        /// hedge needs to spill.</summary>
        static readonly string[] Slices =
        {
            "Pizza_S.002", "Pizza_S.003", "Pizza_S.004", "Pizza_S.005", "Pizza_S.006",
            "Pizza_S.007", "Pizza_S.008", "Pizza_S.009", "Pizza_S.010", "Pizza_S.011",
        };

        /// <summary>The box is ONE prefab with `Tray` and `Lid` children — see
        /// SaveBox. There is no separate lid asset: the assembled height is the
        /// number every consumer needs, and two prefabs meant three places
        /// measuring the tray alone.</summary>
        public const string BoxPrefab = "pizza_box";
        public const string ToppingPrefix = "pizza_top_";
        public const string SlicePrefix = "pizza_slice_";

        [MenuItem("PSX Racing/Bake Pizza Cargo")]
        public static void Bake()
        {
            var pack = AssetDatabase.LoadAssetAtPath<GameObject>(PackFbx);
            if (pack == null) { Debug.LogError("[PizzaCargo] props pack missing at " + PackFbx); return; }

            if (!AssetDatabase.IsValidFolder(Root + "/Resources"))
                AssetDatabase.CreateFolder(Root, "Resources");
            if (!AssetDatabase.IsValidFolder(ResDir))
                AssetDatabase.CreateFolder(Root + "/Resources", "PizzaCargo");

            var inst = (GameObject)Object.Instantiate(pack);
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            inst.transform.localScale = Vector3.one;

            // The scale comes off the BOX, and everything else wears the same
            // one — a pizza scaled to its own "correct" diameter would not fit
            // the box it came out of.
            var boxSrc = Find(inst, "Pizza_box.001") ?? Find(inst, "Pizza_box.003");
            if (boxSrc == null) { Debug.LogError("[PizzaCargo] no Pizza_box in the pack"); Object.DestroyImmediate(inst); return; }

            var raw = WorldBounds(boxSrc);
            float packWidth = Mathf.Max(raw.size.x, raw.size.z);
            float scale = packWidth > 0.01f ? BoxWidthM / packWidth : 1f;
            Debug.Log("[PizzaCargo] pack box measures " + raw.size.ToString("0.000") +
                      " -> scale " + scale.ToString("0.000"));

            // What IS the box? The pack's Pizza_box.001 carries two renderers
            // and nothing says which is which, so they are split by HEIGHT: the
            // lid is the part whose centre sits above the pair's midline. If it
            // turns out to be one piece, the lid prefab simply is not written
            // and the runtime keeps the box shut, which is a worse-looking
            // crash rather than a broken one.
            var parts = new List<Renderer>(boxSrc.GetComponentsInChildren<MeshRenderer>(true));
            foreach (var r in parts)
                Debug.Log("[PizzaCargo]   box part '" + r.name + "' size " +
                          r.bounds.size.ToString("0.000") + " centre y " + r.bounds.center.y.ToString("0.000"));

            // ONE prefab, assembled: a `Tray` and a `Lid` under a single root.
            //
            // Assembled rather than two prefabs because the assembled HEIGHT is
            // the number everything downstream needs — the stack pitch in the
            // player's hands, the stack pitch on the passenger seat, and the
            // interior the pizza has to fit inside. Two prefabs meant three
            // places measuring the tray alone and stacking boxes 6 mm into each
            // other's lids, which the solver resolves by firing the top one
            // across the car on frame one.
            // WHICH PART IS THE LID: the one with the bigger FOOTPRINT, because
            // a lid slips over a tray. It is not the one that sits higher.
            //
            // Splitting by height was wrong and the numbers said so out loud:
            // the tray measures 0.047 tall, the lid 0.055, and the pack's own
            // assembly of the two is 0.053. A pair that overlaps almost
            // completely is not a base and a lid stacked on it — it is an OUTER
            // SHELL with a liner nested inside. Treating them as stacked, and
            // then "correcting" the lid up onto the rim, made a closed pizza box
            // 8.8 cm tall: three and a half inches, which is what the owner saw
            // ("the pizza boxes seem very tall when closed"). The pack had it
            // right all along; both parts keep the positions it gave them.
            int baked = 0;
            var trayParts = new List<Renderer>(parts);
            var lidParts = new List<Renderer>();
            if (parts.Count >= 2)
            {
                Renderer widest = parts[0];
                foreach (var r in parts)
                    if (Foot(r) > Foot(widest)) widest = r;
                trayParts.Clear();
                foreach (var r in parts) (r == widest ? lidParts : trayParts).Add(r);
                Debug.Log("[PizzaCargo] lid is '" + widest.name + "' (" +
                          Foot(widest).ToString("0.000") + " m across)");
            }
            if (SaveBox(trayParts, lidParts, scale)) baked++;

            for (int i = 0; i < Toppings.Length; i++)
            {
                var src = Find(inst, Toppings[i]);
                if (src == null) { Debug.LogWarning("[PizzaCargo] missing topping " + Toppings[i]); continue; }
                if (SaveFlat(new List<Renderer>(src.GetComponentsInChildren<MeshRenderer>(true)),
                             ToppingPrefix + i, scale, seatOnBase: true)) baked++;
            }

            for (int i = 0; i < Slices.Length; i++)
            {
                var src = Find(inst, Slices[i]);
                if (src == null) continue;
                // Slices keep the WHOLE pizza's datum, so a spilled box drops
                // ten wedges in the ring they were cut in rather than ten
                // wedges all stacked on the same spot.
                if (SaveFlat(new List<Renderer>(src.GetComponentsInChildren<MeshRenderer>(true)),
                             SlicePrefix + i, scale, seatOnBase: true)) baked++;
            }

            if (SaveBottle(inst)) baked++;

            Object.DestroyImmediate(inst);
            AssetDatabase.SaveAssets();
            Debug.Log("[PizzaCargo] baked " + baked + " prefabs -> " + ResDir);
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// The box: a flat tray with its lid seated ON THE RIM, saved as one
        /// prefab whose bounds are the closed box's real height.
        ///
        /// The lid is LIFTED rather than left where the pack put it. In the
        /// showcase scene its printed panel sits down inside the tray's rim —
        /// which photographs as an open box with a picture lying in it, not as a
        /// closed box — so it is seated on the rim with a quarter of its own
        /// depth of overlap, the way a real pizza lid's skirt sits over the
        /// tray. A deliberate correction to the pack, written down because it is
        /// the one place here that does not simply take the model's word.
        /// </summary>
        /// <summary>Plan footprint of one renderer, for telling a lid from the
        /// tray it slips over.</summary>
        static float Foot(Renderer r) => Mathf.Max(r.bounds.size.x, r.bounds.size.z);

        /// <summary>Bounds of a set of renderers where they stand in the pack,
        /// without moving anything.</summary>
        static Bounds Peek(List<Renderer> parts)
        {
            var b = parts[0].bounds;
            foreach (var r in parts) b.Encapsulate(r.bounds);
            return b;
        }

        static bool SaveBox(List<Renderer> trayParts, List<Renderer> lidParts, float scale)
        {
            if (trayParts == null || trayParts.Count == 0) return false;

            var holder = new GameObject(BoxPrefab);
            var tray = Assemble(trayParts, "Tray");
            tray.transform.SetParent(holder.transform, true);
            var tb = WorldBounds(tray.transform);
            // Seat the whole assembly on the BOX's base, not the tray's — the
            // lid is an outer shell and may reach below the liner.
            float trayShift = lidParts.Count > 0
                             ? Mathf.Min(tb.min.y, Peek(lidParts).min.y) : tb.min.y;
            tray.transform.position += new Vector3(-tb.center.x, -trayShift, -tb.center.z);

            if (lidParts.Count > 0)
            {
                // The lid keeps the pack's own vertical placement — only the
                // plan position is recentred, by the SAME shift the tray got, so
                // the two stay assembled exactly as the artist made them.
                var lid = Assemble(lidParts, "Lid");
                lid.transform.SetParent(holder.transform, true);
                var lb = WorldBounds(lid.transform);
                lid.transform.position += new Vector3(-lb.center.x, -trayShift, -lb.center.z);
            }

            PSXRacingBuilder.ConvertToPSXMaterials(holder);
            holder.transform.localScale = Vector3.one * scale;
            var closed = WorldBounds(holder.transform);
            Debug.Log("[PizzaCargo] assembled box " + closed.size.ToString("0.000") +
                      " (tray " + (tb.size.y * scale).ToString("0.000") + " m)");

            PrefabUtility.SaveAsPrefabAsset(holder, ResDir + "/" + BoxPrefab + ".prefab");
            Object.DestroyImmediate(holder);
            return true;
        }

        /// <summary>Copy a set of renderers into one object, keeping the world
        /// transform the pack gave them — which is the only thing that makes
        /// these lie flat, and the thing a naive re-parent throws away.</summary>
        static GameObject Assemble(List<Renderer> parts, string name)
        {
            var go = new GameObject(name);
            foreach (var r in parts)
            {
                var copy = (GameObject)Object.Instantiate(r.gameObject);
                copy.name = r.name;
                foreach (var c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                copy.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                copy.transform.localScale = r.transform.lossyScale;
                copy.transform.SetParent(go.transform, true);
            }
            return go;
        }

        /// <summary>
        /// Save one part, laid FLAT, scaled, and seated with its base at y=0.
        ///
        /// "Flat" is measured, not assumed: whichever local axis is thinnest is
        /// rotated to +Y. The pack's parts are top-level children of a showcase
        /// scene and carry whatever rotation put them on a shelf, and
        /// re-parenting drops that rotation on the floor — which is precisely
        /// how the carried box ended up held on its edge like a briefcase.
        /// </summary>
        static bool SaveFlat(List<Renderer> parts, string name, float scale,
                             bool seatOnBase, Bounds? datum = null)
        {
            if (parts == null || parts.Count == 0) return false;

            var holder = new GameObject(name);
            var pivot = new GameObject("Mesh");
            pivot.transform.SetParent(holder.transform, false);

            foreach (var r in parts)
            {
                var copy = (GameObject)Object.Instantiate(r.gameObject);
                copy.name = r.name;
                foreach (var c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                // World transform preserved, THEN re-parented keeping it: the
                // pack's own chain is what makes these lie flat, and it is only
                // knowable in world space once the pack is stood up.
                copy.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                copy.transform.localScale = r.transform.lossyScale;
                copy.transform.SetParent(pivot.transform, true);
            }

            var b = datum ?? WorldBounds(holder.transform);
            // Recentre on the datum's footprint, base to y=0.
            var shift = new Vector3(-b.center.x, seatOnBase ? -b.min.y : -b.center.y, -b.center.z);
            pivot.transform.position += shift;

            // Flatness check, after recentring so the numbers mean something.
            var flat = WorldBounds(holder.transform);
            if (flat.size.y > flat.size.x || flat.size.y > flat.size.z)
            {
                // Thinnest axis is not up. Turn it up, and say so — a silent
                // correction here is a lie the next reader has to rediscover.
                Vector3 s = flat.size;
                Quaternion turn = s.x <= s.y && s.x <= s.z
                                ? Quaternion.Euler(0f, 0f, 90f)      // x is thin
                                : Quaternion.Euler(90f, 0f, 0f);     // z is thin
                pivot.transform.rotation = turn * pivot.transform.rotation;
                var re = WorldBounds(holder.transform);
                pivot.transform.position += new Vector3(-re.center.x, -re.min.y, -re.center.z);
                Debug.Log("[PizzaCargo] " + name + " was on its edge (" + s.ToString("0.000") +
                          ") - turned flat");
            }

            PSXRacingBuilder.ConvertToPSXMaterials(holder);
            holder.transform.localScale = Vector3.one * scale;

            PrefabUtility.SaveAsPrefabAsset(holder, ResDir + "/" + name + ".prefab");
            Object.DestroyImmediate(holder);
            return true;
        }


        /// <summary>
        /// How tall a 2 litre bottle really is, in metres.
        ///
        /// 33 cm to the cap, which is a standard PET two-litre. The same
        /// argument as BoxWidthM: derived as a SCALE from whatever the pack's
        /// mesh measures, because this pack is oversized and a bottle that
        /// arrives at the pack's own size is a fire extinguisher on the seat.
        /// </summary>
        public const float BottleHeightM = 0.33f;

        /// <summary>
        /// Candidate names for the drink, best first.
        ///
        /// A list rather than a name because the pack labels its drinks four
        /// different ways and only some of them are bottles — vessel_Soda is a
        /// CRATE and Can_Soda is a can, and both would bake without complaint
        /// and put a red box on the passenger seat. The shape test below is
        /// what actually decides; this only sets the order it asks in.
        /// </summary>
        static readonly string[] BottleCandidates =
        {
            "Soft_drinks_01", "Soft_drinks", "Soft_drinks_01.001", "Soft_drinks.001",
            "Soft_drinks_01.002", "Soft_drinks.002", "Soft_drinks_c",
        };

        public const string BottlePrefab = "soda_bottle";

        /// <summary>
        /// THE 2 LITRE BOTTLE, BUILT RATHER THAN FOUND.
        ///
        /// It used to be found: the first "Soft_drinks" object in the props
        /// pack taller than it was wide. That is a small glass cola bottle,
        /// and the owner, shown it, drew a ring round something else — the
        /// second row of a grocery shelf, tall PET bottles with coloured
        /// labels. Every drink in every pack in the project was then measured
        /// and photographed (DrinkProbe): the pizzeria pack's soft drinks are
        /// glass bottles, crates, cans and a fountain cup, the house pack has
        /// one bottle and a milk carton, the gas station a fridge texture. The
        /// bottle asked for is not in the project.
        ///
        /// So it is a lathe: base, body, shoulder, neck, cap, twelve sides —
        /// which is the whole silhouette of a 2 litre bottle and about as many
        /// polygons as this renderer wants — and a sixteen by sixty-four label
        /// painted here: drink colour on the body, a contrasting band with a
        /// stripe through it for the label, a cap. Four of them, because the
        /// shelf in the photograph is four colours, and two identical bottles
        /// on a seat read as one extruded object. Saved under the same
        /// contract the pack bottle was — base at y = 0, centred in plan,
        /// BottleHeightM tall — so nothing that stands one on a seat or lays
        /// one on a lid knows the difference. If the grocery pack that shelf
        /// came from ever lands in Art/, this is the one method to point at it.
        /// </summary>
        static bool SaveBottle(GameObject inst)
        {
            // (drink colour, label colour, cap colour) — cola, lemon-lime,
            // orange, cherry: the four the photograph shows.
            var looks = new (Color body, Color label, Color cap)[]
            {
                (new Color(0.20f, 0.10f, 0.06f), new Color(0.80f, 0.12f, 0.10f), new Color(0.80f, 0.12f, 0.10f)),
                (new Color(0.62f, 0.86f, 0.50f), new Color(0.10f, 0.52f, 0.22f), new Color(0.10f, 0.52f, 0.22f)),
                (new Color(0.96f, 0.58f, 0.12f), new Color(0.12f, 0.30f, 0.72f), new Color(0.96f, 0.58f, 0.12f)),
                (new Color(0.68f, 0.10f, 0.16f), new Color(0.96f, 0.84f, 0.22f), new Color(0.92f, 0.92f, 0.92f)),
            };

            int made = 0;
            for (int v = 0; v < looks.Length; v++)
            {
                string name = v == 0 ? BottlePrefab : BottlePrefab + "_" + v;
                var tex = LabelTexture(name, looks[v].body, looks[v].label, looks[v].cap);
                var mesh = BottleMesh();
                AssetDatabase.CreateAsset(mesh, ResDir + "/" + name + "_mesh.asset");

                var holder = new GameObject(name);
                var pivot = new GameObject("Mesh");
                pivot.transform.SetParent(holder.transform, false);
                pivot.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = pivot.AddComponent<MeshRenderer>();
                // A throwaway Standard material carrying the label: the PSX
                // converter reads a material's mainTexture and rebuilds it,
                // which is the same road every pack material takes.
                var tmp = new Material(Shader.Find("Standard")) { mainTexture = tex, name = name };
                mr.sharedMaterial = tmp;

                // Already based at y = 0 and centred — the lathe is built that
                // way — but measured and corrected anyway, the same as the pack
                // bottle was, so the contract holds if the profile changes.
                var got = WorldBounds(holder.transform);
                pivot.transform.position += new Vector3(-got.center.x, -got.min.y, -got.center.z);

                PSXRacingBuilder.ConvertToPSXMaterials(holder);
                PrefabUtility.SaveAsPrefabAsset(holder, ResDir + "/" + name + ".prefab");
                Object.DestroyImmediate(holder);
                Object.DestroyImmediate(tmp);
                made++;
            }
            Debug.Log("[PizzaCargo] built " + made + " two-litre bottles (lathe, " +
                      BottleHeightM.ToString("0.00") + " m)");
            return made > 0;
        }

        /// <summary>
        /// The bottle's outline, turned round its axis. Radii and heights in
        /// metres for a real 2 litre: 105 mm across, 330 mm tall, a shoulder
        /// that starts two thirds of the way up, a 4 cm neck and a cap.
        /// Twelve sides — PSX — and a closed top and bottom.
        /// </summary>
        static Mesh BottleMesh()
        {
            var profile = new (float y, float r)[]
            {
                (0.000f, 0.040f), (0.012f, 0.052f), (0.200f, 0.052f), (0.250f, 0.038f),
                (0.285f, 0.021f), (0.308f, 0.021f), (0.310f, 0.017f), (0.330f, 0.017f),
            };
            const int Sides = 12;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Rings. One extra column so the UV seam closes at u = 1.
            for (int i = 0; i < profile.Length; i++)
                for (int s = 0; s <= Sides; s++)
                {
                    float a = s / (float)Sides * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(a) * profile[i].r, profile[i].y,
                                          Mathf.Sin(a) * profile[i].r));
                    uvs.Add(new Vector2(s / (float)Sides, profile[i].y / BottleHeightM));
                }
            int cols = Sides + 1;
            for (int i = 0; i < profile.Length - 1; i++)
                for (int s = 0; s < Sides; s++)
                {
                    int a = i * cols + s, b = a + 1, c = a + cols, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            // Base and cap discs.
            int bottom = verts.Count;
            verts.Add(new Vector3(0f, profile[0].y, 0f));
            uvs.Add(new Vector2(0.5f, 0f));
            for (int s = 0; s < Sides; s++)
            { tris.Add(bottom); tris.Add(s + 1); tris.Add(s); }
            int top = verts.Count;
            verts.Add(new Vector3(0f, profile[profile.Length - 1].y, 0f));
            uvs.Add(new Vector2(0.5f, 1f));
            int lastRing = (profile.Length - 1) * cols;
            for (int s = 0; s < Sides; s++)
            { tris.Add(top); tris.Add(lastRing + s); tris.Add(lastRing + s + 1); }

            var mesh = new Mesh { name = "soda_bottle" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The label, painted: v runs bottom to top. Drink colour on the body,
        /// a darker foot, a band of label colour from a third of the way up to
        /// just past the middle with a pale stripe through it, the neck a
        /// shade lighter, the cap its own colour. Sixteen by sixty-four, point
        /// filtered, no mips — the renderer's own rules for a texture.
        /// </summary>
        static Texture2D LabelTexture(string name, Color body, Color label, Color cap)
        {
            const int W = 16, H = 64;
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float v = (y + 0.5f) / H;
                Color c;
                if (v < 0.04f) c = body * 0.55f;
                else if (v < 0.30f) c = body;
                else if (v < 0.60f)
                {
                    c = label;
                    // The stripe, and a thin dark rule at each edge of the
                    // band so it reads as a printed label and not a colour
                    // change in the plastic.
                    if (v > 0.42f && v < 0.48f) c = Color.Lerp(label, Color.white, 0.75f);
                    if (v < 0.315f || v > 0.585f) c = label * 0.6f;
                }
                else if (v < 0.86f) c = body;
                else if (v < 0.94f) c = Color.Lerp(body, Color.white, 0.35f);
                else c = cap;
                c.a = 1f;
                for (int x = 0; x < W; x++) px[y * W + x] = c;
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();

            string path = ResDir + "/" + name + "_label.png";
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                imp.filterMode = FilterMode.Point;
                imp.mipmapEnabled = false;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Transform Find(GameObject root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        static Bounds WorldBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<MeshRenderer>(true);
            if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
