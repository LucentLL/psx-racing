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

            if (SaveBottle()) baked++;

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

        public const string BottlePrefab = "soda_bottle";

        /// <summary>The owner's bottles, cut out of his "All" pack by
        /// tools/sodas/export_sodas.py: four meshes named soda_2l_0..3 and the
        /// one 256 px sheet all four are mapped onto.</summary>
        const string SodaFbx = Root + "/Art/LifeSim/Groceries/Sodas2L.fbx";
        const string SodaSheet = Root + "/Art/LifeSim/Groceries/Sodas2L.png";

        /// <summary>
        /// THE 2 LITRE BOTTLES THE OWNER ASKED FOR, third time of asking.
        ///
        /// First this FOUND one: the first "Soft_drinks" object in the pizzeria
        /// pack taller than it was wide, which is a small glass cola bottle.
        /// Shown it, the owner drew a ring round something else — a grocery
        /// shelf of tall PET bottles with printed labels. So every drink in
        /// every pack IN THE PROJECT was measured (DrinkProbe), none of them was
        /// that, and this BUILT one instead: a twelve-sided lathe with a label
        /// painted in code. It came back as "generic looking glass bottles that
        /// are hollow and transparent on the bottom", and both halves were
        /// fair. The painted label was three flat colours. And the lathe's two end
        /// discs were both wound facing INTO the bottle, so from underneath the
        /// base was back-face culled and a bottle lying on the seat showed the
        /// inside of itself through its own foot.
        ///
        /// THE SEARCH STOPPED AT THE PROJECT'S EDGE, and that was the actual
        /// mistake. The ringed bottles were never in it. They are in the
        /// owner's art folder, in a household pack called "All" (672 objects in
        /// one FBX), as Soda .. Soda_15 — four rows of four, and the second row
        /// from the front is the four uniform two-litres he named. "Not in the
        /// project" was true and was not the question; when the owner points at
        /// a picture, the thing in the picture is on his disk.
        ///
        /// So this is a loader again. Four looks, because two identical bottles
        /// on a seat read as one extruded object; the same contract the last
        /// two kept — base at y = 0, centred in plan, BottleHeightM tall — so
        /// nothing that stands one on a seat or lays one down knows the
        /// difference; and real closed meshes, so the foot is a foot.
        ///
        /// Stood UP by measurement, like everything here: whichever axis is
        /// longest is turned to +Y. SaveFlat does the opposite on purpose — it
        /// is right for a pizza and backwards for the one prop whose height is
        /// the point — and the exporter's axis conversion is exactly the kind
        /// of thing that is correct until somebody re-exports.
        /// </summary>
        static bool SaveBottle()
        {
            var pack = AssetDatabase.LoadAssetAtPath<GameObject>(SodaFbx);
            if (pack == null)
            {
                Debug.LogError("[PizzaCargo] no bottles at " + SodaFbx +
                               " - run tools/sodas/export_sodas.py and build_atlas.py");
                return false;
            }
            var sheet = SodaSheetTexture();
            if (sheet == null) { Debug.LogError("[PizzaCargo] no bottle sheet at " + SodaSheet); return false; }

            var inst = (GameObject)Object.Instantiate(pack);
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            inst.transform.localScale = Vector3.one;

            // A throwaway Standard material carrying the sheet: the PSX
            // converter reads a material's mainTexture and rebuilds it, which
            // is the same road every pack material takes. Assigned here rather
            // than left to the FBX importer's texture search, which finds a
            // file by name when it feels like it.
            var tmp = new Material(Shader.Find("Standard")) { mainTexture = sheet, name = "Sodas2L" };

            int made = 0;
            for (int v = 0; v < PSXRacing.PizzaCargoBakerNames.BottleVariants; v++)
            {
                var src = Find(inst, "soda_2l_" + v);
                if (src == null) { Debug.LogWarning("[PizzaCargo] the pack has no soda_2l_" + v); continue; }

                string name = v == 0 ? BottlePrefab : BottlePrefab + "_" + v;

                // RIGHT SIDE OUT, or it is not baked. See InsideOut.
                var srcMesh = src.GetComponentInChildren<MeshFilter>(true);
                if (srcMesh == null || srcMesh.sharedMesh == null || InsideOut(srcMesh.sharedMesh))
                {
                    Debug.LogError("[PizzaCargo] " + src.name + " is INSIDE OUT (or has no mesh) - " +
                                   "re-run tools/sodas/export_sodas.py; not baking a hollow bottle");
                    continue;
                }

                var holder = new GameObject(name);
                var pivot = new GameObject("Mesh");
                pivot.transform.SetParent(holder.transform, false);

                var copy = (GameObject)Object.Instantiate(src.gameObject);
                copy.name = src.name;
                foreach (var c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                copy.transform.SetPositionAndRotation(src.position, src.rotation);
                copy.transform.localScale = src.lossyScale;
                copy.transform.SetParent(pivot.transform, true);
                foreach (var r in copy.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterial = tmp;

                // Longest axis up.
                var b = WorldBounds(holder.transform);
                if (b.size.y < b.size.x || b.size.y < b.size.z)
                {
                    Quaternion turn = b.size.x >= b.size.z ? Quaternion.Euler(0f, 0f, 90f)
                                                           : Quaternion.Euler(-90f, 0f, 0f);
                    pivot.transform.rotation = turn * pivot.transform.rotation;
                    Debug.Log("[PizzaCargo] " + name + " arrived lying down (" +
                              b.size.ToString("0.000") + ") - stood up");
                    b = WorldBounds(holder.transform);
                }
                pivot.transform.position += new Vector3(-b.center.x, -b.min.y, -b.center.z);

                float scale = b.size.y > 0.01f ? BottleHeightM / b.size.y : 1f;
                PSXRacingBuilder.ConvertToPSXMaterials(holder);
                holder.transform.localScale = Vector3.one * scale;
                var got = WorldBounds(holder.transform);
                Debug.Log("[PizzaCargo] " + name + " <- " + src.name + "  " + got.size.ToString("0.000") +
                          " m, base y " + got.min.y.ToString("0.000"));

                PrefabUtility.SaveAsPrefabAsset(holder, ResDir + "/" + name + ".prefab");
                Object.DestroyImmediate(holder);
                made++;
            }

            Object.DestroyImmediate(inst);
            Object.DestroyImmediate(tmp);
            RemoveLatheLeftovers();
            Debug.Log("[PizzaCargo] baked " + made + " two-litre bottles from the owner's pack (" +
                      BottleHeightM.ToString("0.00") + " m)");
            return made > 0;
        }

        /// <summary>
        /// Is this closed mesh wound INSIDE OUT?
        ///
        /// The first export of these bottles was. The pack places them with a
        /// negative scale on all three axes, the exporter baked that mirror into
        /// the vertices without turning the triangles back round, and the
        /// renderer — which culls back faces — drew the inside of each bottle's
        /// far wall and nothing of its near one: "these bottles look hollow like
        /// they're missing a side". Nothing here could have noticed, because
        /// nothing here asked. A picture did not show it either: the label is a
        /// planar projection, so the inside of the back is the same picture as
        /// the outside of the front.
        ///
        /// A closed mesh has a signed volume — the sum of v0 . (v1 x v2) over
        /// its triangles — and its sign is which way the faces point. WHICH sign
        /// is "out" depends on the engine's handedness and winding, so it is not
        /// typed in: it is read off a mesh Unity itself guarantees is right.
        /// MESH space, deliberately. A negative scale on the TRANSFORM is fine;
        /// Unity flips its culling to match. It is only a mirror baked into the
        /// vertices that nothing downstream can see.
        /// </summary>
        internal static bool InsideOut(Mesh mesh)
        {
            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            float right = SignedVolume(probe.GetComponent<MeshFilter>().sharedMesh);
            Object.DestroyImmediate(probe);
            return SignedVolume(mesh) * right <= 0f;
        }

        static float SignedVolume(Mesh mesh)
        {
            var v = mesh.vertices;
            var t = mesh.triangles;
            double sum = 0.0;
            for (int i = 0; i + 2 < t.Length; i += 3)
                sum += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]])) / 6.0;
            return (float)sum;
        }

        /// <summary>The sheet, imported by the renderer's own rules for a
        /// texture: point filtered, no mips, clamped, uncompressed. Checked
        /// before it is changed — SaveAndReimport re-encodes even when nothing
        /// moved, and an unconditional one on every bake is how the audio
        /// importer once cost every build five minutes.</summary>
        static Texture2D SodaSheetTexture()
        {
            if (AssetImporter.GetAtPath(SodaSheet) is TextureImporter imp &&
                (imp.filterMode != FilterMode.Point || imp.mipmapEnabled ||
                 imp.wrapMode != TextureWrapMode.Clamp ||
                 imp.textureCompression != TextureImporterCompression.Uncompressed))
            {
                imp.filterMode = FilterMode.Point;
                imp.mipmapEnabled = false;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(SodaSheet);
        }

        /// <summary>
        /// What the lathe left behind: four meshes, four painted labels and the
        /// four materials made from them. Nothing references them once the
        /// prefabs above are rewritten, and an unreferenced asset under
        /// Resources still SHIPS — Unity cannot know a Resources.Load string
        /// will never name it. Deleted through the AssetDatabase so the metas
        /// go with them.
        /// </summary>
        static void RemoveLatheLeftovers()
        {
            for (int v = 0; v < 4; v++)
            {
                string name = v == 0 ? BottlePrefab : BottlePrefab + "_" + v;
                AssetDatabase.DeleteAsset(ResDir + "/" + name + "_mesh.asset");
                AssetDatabase.DeleteAsset(ResDir + "/" + name + "_label.png");
                AssetDatabase.DeleteAsset(Root + "/Materials/scenery_" + name + "_label_" + name + ".mat");
            }
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
