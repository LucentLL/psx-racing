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
            if (SaveSeats()) baked += SeatSources.Length;

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

        // ------------------------------------------------------------------
        /// <summary>
        /// The owner's seats, from his art folder, one per rung that has one.
        ///
        /// 2026-09-25, the stock seat: "use this as the default car seat that
        /// pizzas and drinks are placed on ... scaled correctly relative to
        /// pizza box size". Built at real size — 0.54 m wide, a cushion 0.39 m
        /// from squab to lip — and it STAYS at real size: a 41 cm box covers
        /// that cushion, which is what one does on a real passenger seat.
        ///
        /// Later the same day, the two buckets for stages 3 and 4. At real size
        /// neither takes a box — both are 35 cm between the bolsters and the
        /// box is 41 — and the owner chose to SCALE THEM UP until it fits:
        /// "the pizza boxes should fit comfortably between the bolsters, but
        /// still have enough room to move side to side". So a bucket is scaled,
        /// uniformly, until its narrowest bolster face stands exactly where the
        /// seat ladder's bolster collider does (SeatSpec.bolsterHalf), which is
        /// what makes the drawn bolster the thing that stops the box.
        /// </summary>
        struct SeatSource
        {
            public string prefab, fbx, atlas;
            /// <summary>The pad a box lies on; its top cap is the pan.</summary>
            public string cushion;
            /// <summary>The pad a box's back meets; its front cap gives the
            /// recline.</summary>
            public string squab;
            /// <summary>The ladder rung whose bolsters this seat is scaled to
            /// meet, or -1 for "real size, as modelled".</summary>
            public int fitStage;
        }

        const string SeatDir = Root + "/Art/LifeSim/Seat/";
        public const string SeatPrefab = "car_seat";

        static readonly SeatSource[] SeatSources =
        {
            new SeatSource { prefab = SeatPrefab, fbx = SeatDir + "psx_seat.fbx", atlas = SeatDir + "seat_atlas.png",
                             cushion = "Cushion_Center", squab = "Back_Lower_Pad", fitStage = -1 },
            new SeatSource { prefab = SeatPrefab + "_3", fbx = SeatDir + "amateur_bucket.fbx", atlas = SeatDir + "bucket_atlas.png",
                             cushion = "Seat_Cushion", squab = "Back_Cushion", fitStage = 3 },
            // The THIGH pad, not the pelvis one: it stands 1.9 cm proud of the
            // pelvis pad, and a rigid box lies on the highest thing under it.
            new SeatSource { prefab = SeatPrefab + "_4", fbx = SeatDir + "professional_bucket_seat.fbx", atlas = SeatDir + "pro_seat_atlas.png",
                             cushion = "Thigh_Cushion", squab = "Lumbar_Pad", fitStage = 4 },
        };

        static bool SaveSeats()
        {
            bool all = true;
            foreach (var src in SeatSources) all &= SaveSeat(src);
            return all;
        }

        /// <summary>
        /// One seat, stood up and MEASURED, with the two landmarks the cargo
        /// rig needs written into it as empty children.
        ///
        /// Up and forward are read off the model rather than trusted to an
        /// exporter's axis conversion: up is its tallest axis, pointing away
        /// from the cushion (which is at the bottom); forward is its longer
        /// level axis, pointing toward the cushion (which is at the front).
        ///
        /// `SquabPoint` is where the box's BACK stops, on the cushion plane:
        /// the squab's face, or whatever stands in front of it within a box's
        /// width — the amateur bucket's torso bolsters wrap four centimetres
        /// ahead of its back pad and fourteen off the centreline, and a box
        /// set against the pad would start the run inside them. Its rotation
        /// is the squab's recline. `CushionFront` is the front edge of the
        /// cushion on the same plane. The cushion and squab are PLANE FITS to
        /// the pad caps a box touches, because those are what the pan and back
        /// colliders have to coincide with.
        ///
        /// Children rather than a component, so no new script type has to
        /// keep its GUID across a sandbox mirror.
        /// </summary>
        static bool SaveSeat(SeatSource src)
        {
            // READABLE, before anything is loaded from it: the seat's meshes
            // are its colliders (below), and a WebGL player can only cook a
            // MeshCollider from a mesh it can read. Checked first — a
            // SaveAndReimport re-encodes whether or not anything changed.
            if (AssetImporter.GetAtPath(src.fbx) is ModelImporter mi && !mi.isReadable)
            {
                mi.isReadable = true;
                mi.SaveAndReimport();
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(src.fbx);
            if (model == null) { Debug.LogError("[PizzaCargo] no seat at " + src.fbx); return false; }
            var atlas = PointTexture(src.atlas);

            var holder = new GameObject(src.prefab);
            var pivot = new GameObject("Mesh");
            pivot.transform.SetParent(holder.transform, false);
            var inst = (GameObject)Object.Instantiate(model);
            inst.name = model.name;
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            inst.transform.localScale = Vector3.one;
            inst.transform.SetParent(pivot.transform, true);
            foreach (var col in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);

            // The same road the bottles take: a throwaway Standard material
            // carrying the atlas, which the PSX converter then rebuilds.
            var tmp = new Material(Shader.Find("Standard")) { mainTexture = atlas, name = "Upholstery" };
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++) ms[i] = tmp;
                r.sharedMaterials = ms;
            }

            var cushion = Find(holder, src.cushion);
            var squab = Find(holder, src.squab);
            if (cushion == null || squab == null)
            {
                Debug.LogError("[PizzaCargo] " + src.prefab + " has no '" + src.cushion + "' or '" + src.squab + "'");
                Object.DestroyImmediate(holder); Object.DestroyImmediate(tmp);
                return false;
            }

            // Up and forward, from the model's own shape.
            var b = WorldBounds(holder.transform);
            Vector3 cc = WorldBounds(cushion).center - b.center;
            Vector3 sz = b.size;
            int upAx = sz.x >= sz.y && sz.x >= sz.z ? 0 : sz.y >= sz.z ? 1 : 2;
            int fwAx = -1;
            for (int ax = 0; ax < 3; ax++)
                if (ax != upAx && (fwAx < 0 || sz[ax] > sz[fwAx])) fwAx = ax;
            Vector3 up = Vector3.zero, fwd = Vector3.zero;
            up[upAx] = -Mathf.Sign(cc[upAx]);
            fwd[fwAx] = Mathf.Sign(cc[fwAx]);
            pivot.transform.rotation = Quaternion.Inverse(Quaternion.LookRotation(fwd, up)) *
                                       pivot.transform.rotation;

            // Metres as authored. A unit slip (centimetres, or a scale-0.01
            // FBX) is corrected; the seat's SIZE is the owner's unless a rung
            // below asks for it to fit.
            b = WorldBounds(holder.transform);
            float unit = b.size.y > 10f ? 0.01f : b.size.y < 0.1f ? 100f : 1f;
            if (unit != 1f)
            {
                pivot.transform.localScale *= unit;
                Debug.LogWarning("[PizzaCargo] " + src.prefab + " arrived " + b.size.y.ToString("0.000") +
                                 " units tall - rescaled x" + unit + " to metres");
            }
            Recentre(holder, pivot);

            float boxHalf = BoxWidthM * 0.5f;
            SeatFrame f;
            if (!MeasureSeat(holder, cushion, squab, boxHalf, out f))
            {
                Debug.LogError("[PizzaCargo] could not find " + src.prefab + "'s cushion or squab faces");
                Object.DestroyImmediate(holder); Object.DestroyImmediate(tmp);
                return false;
            }

            // FITTED TO THE LADDER: scaled until the narrowest bolster face
            // beside the box stands where this rung's bolster collider does.
            // Measured again after every step, because the band a box occupies
            // does not scale with the seat.
            float fitScale = 1f;
            if (src.fitStage >= 0)
            {
                float target = PSXRacing.PizzaCargo.SeatAt(src.fitStage).bolsterHalf -
                               PSXRacing.PizzaCargo.BolsterSlabHalf;
                for (int it = 0; it < 6; it++)
                {
                    if (float.IsInfinity(f.channel))
                    {
                        Debug.LogError("[PizzaCargo] " + src.prefab + " has no bolsters beside the box to fit");
                        break;
                    }
                    float k = target / f.channel;
                    if (Mathf.Abs(k - 1f) < 0.001f) break;
                    pivot.transform.localScale *= k;
                    fitScale *= k;
                    Recentre(holder, pivot);
                    MeasureSeat(holder, cushion, squab, boxHalf, out f);
                }
            }
            b = WorldBounds(holder.transform);

            var sp = new GameObject("SquabPoint").transform;
            sp.SetParent(holder.transform, false);
            sp.localPosition = f.rear;
            sp.localRotation = Quaternion.Euler(-f.reclineDeg, 0f, 0f);
            var cf = new GameObject("CushionFront").transform;
            cf.SetParent(holder.transform, false);
            cf.localPosition = f.front;
            cf.localRotation = Quaternion.Euler(-f.slopeDeg, 0f, 0f);
            // What the shop quotes, as measured: x is half the clear width
            // between the bolsters, y how tall they stand above the surface
            // the box lies on. Absent when the seat has no bolsters beside a
            // box (the stock seat's are a centimetre of padding).
            if (!float.IsInfinity(f.channel))
            {
                var bl = new GameObject("Bolster").transform;
                bl.SetParent(holder.transform, false);
                bl.localPosition = new Vector3(f.channel, f.bolsterRise, 0f);
            }

            // THE MODEL IS THE COLLIDER. Owner, 2026-09-25: "physics should
            // match the models. If I think they need to be adjusted I will get
            // new models." So every part — cushion, bolsters, squab, shell —
            // collides as drawn; the rig adds no seat slabs of its own around
            // it. The meshes were made readable at the top of this method.
            //
            // CONVEX WHERE THE PART IS CONVEX, and only there. Every pad and
            // cushion is a lofted, closed, convex block, and as a triangle
            // mesh PhysX snagged a sliding box on the edges between its
            // triangles: a lone box on the stock seat rolled to 40 degrees
            // slid 0.000 m (friction lets go at 35). As convex hulls the same
            // parts slide it 0.15 m, to the door. But a hull of a CONCAVE part
            // is not the part — the professional bucket's side walls dip
            // between a tall back and a low front, and a hull would stand a
            // wall across that dip — so those keep their exact triangles.
            //
            // IN METRES, ON THEIR OWN MESHES. The owner's FBX parts arrive as
            // vertices in hundredths under a x100 node (and the buckets under
            // the fit scale besides), and PhysX cooks a convex hull from the
            // mesh as stored: a 0.4 m pad is 0.004 units there, inside the
            // cooker's weld tolerance, and the hull it made let a box sink
            // through the professional bucket's cushion ("the pizza box is
            // clipping through the seat"). So each part is copied into the
            // seat's own frame in real metres, saved beside the prefab, and
            // collides from a plain child with no scale anywhere above it.
            string colPath = ResDir + "/" + src.prefab + "_colliders.asset";
            AssetDatabase.DeleteAsset(colPath);
            Mesh container = null;
            var colRoot = new GameObject("Colliders").transform;
            colRoot.SetParent(holder.transform, false);
            int hulls = 0, exact = 0;
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = mf.transform.localToWorldMatrix;
                var src2 = mf.sharedMesh;
                var vs = src2.vertices;
                for (int i = 0; i < vs.Length; i++) vs[i] = m.MultiplyPoint3x4(vs[i]);
                var tris = src2.triangles;
                if (m.determinant < 0f)
                    for (int i = 0; i + 2 < tris.Length; i += 3) { int k = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = k; }
                var cm = new Mesh { name = src.prefab + "_" + mf.name };
                cm.vertices = vs;
                cm.triangles = tris;
                cm.RecalculateBounds();
                cm.RecalculateNormals();
                if (container == null) { AssetDatabase.CreateAsset(cm, colPath); container = cm; }
                else AssetDatabase.AddObjectToAsset(cm, container);

                var cgo = new GameObject(mf.name);
                cgo.transform.SetParent(colRoot, false);
                var mc = cgo.AddComponent<MeshCollider>();
                mc.sharedMesh = cm;
                mc.convex = IsConvex(mf, 0.002f);
                if (mc.convex) hulls++; else exact++;
            }
            Debug.Log("[PizzaCargo] " + src.prefab + " colliders: " + hulls + " convex parts, " + exact + " concave kept exact");

            PSXRacingBuilder.ConvertToPSXMaterials(holder);
            float depth = Vector3.Distance(f.rear, f.front);
            Debug.Log("[PizzaCargo] " + src.prefab + " " + b.size.ToString("0.000") + " m (x" +
                      fitScale.ToString("0.00") + "); cushion " + depth.ToString("0.000") + " m deep at " +
                      f.slopeDeg.ToString("0.0") + " deg, squab reclined " + f.reclineDeg.ToString("0.0") +
                      " deg; bolsters " + (float.IsInfinity(f.channel) ? "none" : (2f * f.channel).ToString("0.000") + " m apart") +
                      ", " + f.bolsterRise.ToString("0.000") + " m tall; a " + BoxWidthM.ToString("0.00") +
                      " m box is " + (BoxWidthM / depth).ToString("0.00") + " of the cushion's depth");

            PrefabUtility.SaveAsPrefabAsset(holder, ResDir + "/" + src.prefab + ".prefab");
            Object.DestroyImmediate(holder);
            Object.DestroyImmediate(tmp);
            return true;
        }

        static void Recentre(GameObject holder, GameObject pivot)
        {
            var b = WorldBounds(holder.transform);
            pivot.transform.position += new Vector3(-b.center.x, -b.min.y, -b.center.z);
        }

        struct SeatFrame
        {
            public Vector3 rear, front;
            public float slopeDeg, reclineDeg;
            /// <summary>Half the clear width between the bolsters, beside the
            /// box, over the height a stack occupies. Infinity: no bolsters.</summary>
            public float channel;
            /// <summary>How far the bolsters that set the channel stand above
            /// the cushion.</summary>
            public float bolsterRise;
        }

        /// <summary>
        /// Everything the rig needs, in the holder's metres. A box's
        /// footprint is judged against the cushion PLANE (height above it, not
        /// world y), and only parts that are not the cushion itself count as
        /// something the box can meet.
        /// </summary>
        static bool MeasureSeat(GameObject holder, Transform cushion, Transform squab, float boxHalf,
                                out SeatFrame f)
        {
            f = default;
            if (!FitPlane(cushion, Vector3.up, out float a, out float s) ||
                !FitPlane(squab, Vector3.forward, out float c, out float d)) return false;
            float sy = (a + s * c) / (1f - s * d);
            float squabZ = c + d * sy;

            var cushionVerts = new HashSet<Vector3>(WorldVerts(cushion));
            var all = WorldVerts(holder.transform);
            float H(Vector3 v) => v.y - (a + s * v.z);

            // Front: the furthest-forward point of anything at cushion height.
            // Any width: the professional bucket's thigh pad is bevelled in to
            // 21 cm off the centreline, so "under the box" found nothing and
            // measured its cushion one millimetre deep.
            float frontZ = squabZ;
            foreach (var v in all)
                if (Mathf.Abs(H(v)) < 0.03f) frontZ = Mathf.Max(frontZ, v.z);
            float midZ = (squabZ + frontZ) * 0.5f;

            // Rear: whatever stands in the box's way behind it, above the
            // cushion — the squab, or bolsters wrapped in front of it.
            float RearStop(float above)
            {
                float r = squabZ;
                foreach (var v in all)
                {
                    if (cushionVerts.Contains(v) || v.z > midZ || Mathf.Abs(v.x) > boxHalf + 0.005f) continue;
                    float h = H(v);
                    if (h > above && h < 0.25f) r = Mathf.Max(r, v.z);
                }
                return r;
            }
            float rearZ = RearStop(0.02f);

            // THE SURFACE A BOX RESTS ON is the highest thing under it, not
            // the pad the plane was fitted to: the stock seat's cushion
            // bolsters stand a centimetre proud of its centre pad, and now
            // that the seat's own meshes are the colliders a box lies on
            // THEM. A stack placed on the centre pad would start the run
            // inside them.
            float z0 = rearZ + PSXRacing.PizzaCargo.SquabGapM, z1 = z0 + 2f * boxHalf;
            float lift = 0f;
            foreach (var v in all)
            {
                if (Mathf.Abs(v.x) > boxHalf || v.z < z0 || v.z > z1) continue;
                float h = H(v);
                if (h > -0.01f && h < 0.03f) lift = Mathf.Max(lift, h);
            }
            a += lift;
            // And measured from there, anything above that surface is in the
            // box's way, however low.
            rearZ = RearStop(0.003f);

            // The channel: the nearest bolster face to the centreline beside
            // where the box will lie, over the stack's height.
            z0 = rearZ + PSXRacing.PizzaCargo.SquabGapM; z1 = z0 + 2f * boxHalf;
            float channel = float.PositiveInfinity, rise = 0f;
            foreach (var v in all)
            {
                if (cushionVerts.Contains(v) || v.z < z0 || v.z > z1 || Mathf.Abs(v.x) < 0.03f) continue;
                float h = H(v);
                if (h > 0.003f && h < 0.20f) channel = Mathf.Min(channel, Mathf.Abs(v.x));
            }
            if (!float.IsInfinity(channel))
                foreach (var v in all)
                    if (!cushionVerts.Contains(v) && v.z >= z0 && v.z <= z1 &&
                        Mathf.Abs(v.x) < channel + 0.03f && Mathf.Abs(v.x) >= channel - 0.001f)
                        rise = Mathf.Max(rise, H(v));

            f.rear = new Vector3(0f, a + s * rearZ, rearZ);
            f.front = new Vector3(0f, a + s * frontZ, frontZ);
            f.slopeDeg = Mathf.Atan(s) * Mathf.Rad2Deg;
            f.reclineDeg = Mathf.Atan(-d) * Mathf.Rad2Deg;
            f.channel = channel;
            f.bolsterRise = rise;
            return true;
        }

        /// <summary>
        /// Is this closed mesh convex? It is if no vertex stands outside the
        /// plane of any of its faces, by more than <paramref name="tol"/>
        /// METRES. Winding-blind: a face's "outside" is whichever side the
        /// mesh's centroid is not on.
        ///
        /// In world space, because a Blender FBX arrives as vertices in
        /// hundredths under a x100 node: in mesh units every face's cross
        /// product fell under the degenerate cut-off, every face was skipped,
        /// and every part — the pro bucket's walls, 30 cm concave — read
        /// convex.
        /// </summary>
        static bool IsConvex(MeshFilter mf, float tol)
        {
            var mesh = mf.sharedMesh;
            var m = mf.transform.localToWorldMatrix;
            var v = mesh.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
            var t = mesh.triangles;
            if (v.Length == 0 || t.Length < 12) return false;
            Vector3 c = Vector3.zero;
            foreach (var p in v) c += p;
            c /= v.Length;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 a = v[t[i]];
                Vector3 n = Vector3.Cross(v[t[i + 1]] - a, v[t[i + 2]] - a);
                if (n.sqrMagnitude < 1e-12f) continue;
                n.Normalize();
                if (Vector3.Dot(c - a, n) > 0f) n = -n;   // outward
                foreach (var p in v)
                    if (Vector3.Dot(p - a, n) > tol) return false;
            }
            return true;
        }

        /// <summary>The signed world axis nearest a direction.</summary>
        static Vector3 Snap(Vector3 v)
        {
            Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            if (a.x >= a.y && a.x >= a.z) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
            if (a.y >= a.z) return new Vector3(0f, Mathf.Sign(v.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        static List<Vector3> WorldVerts(Transform part)
        {
            var list = new List<Vector3>();
            foreach (var mf in part.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices) list.Add(m.MultiplyPoint3x4(v));
            }
            return list;
        }

        /// <summary>
        /// The CAP of a padded part that faces <paramref name="facing"/>, as a
        /// line in the seat's side view (up: y = k0 + k1 z; forward:
        /// z = k0 + k1 y).
        ///
        /// A pad is rings of vertices lofted into a solid, so it has two big
        /// flat caps — the one a box touches and the one against the seat —
        /// and bevels between the rings that point MOSTLY the same way (a 12%
        /// shrink over 9 mm is only 27 degrees off). Averaging "faces that
        /// point roughly up" mixes all three. So triangles are grouped into
        /// exact planes, planes too small to be a cap are dropped, and of what
        /// is left the one furthest along <paramref name="facing"/> is the
        /// face. Either winding: a mirrored export still reads.
        /// </summary>
        static bool FitPlane(Transform part, Vector3 facing, out float k0, out float k1)
        {
            k0 = k1 = 0f;
            bool upward = facing.y > 0.5f;
            var normals = new List<Vector3>();
            var offsets = new List<float>();
            var areas = new List<float>();
            var members = new List<List<Vector3>>();
            foreach (var mf in part.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = mf.transform.localToWorldMatrix;
                var vs = mf.sharedMesh.vertices;
                var ts = mf.sharedMesh.triangles;
                for (int i = 0; i + 2 < ts.Length; i += 3)
                {
                    Vector3 p0 = m.MultiplyPoint3x4(vs[ts[i]]);
                    Vector3 p1 = m.MultiplyPoint3x4(vs[ts[i + 1]]);
                    Vector3 p2 = m.MultiplyPoint3x4(vs[ts[i + 2]]);
                    Vector3 cr = Vector3.Cross(p1 - p0, p2 - p0);
                    float area = cr.magnitude * 0.5f;
                    if (area < 1e-8f) continue;
                    Vector3 nrm = cr.normalized;
                    if (Vector3.Dot(nrm, facing) < 0f) nrm = -nrm;
                    if (Vector3.Dot(nrm, facing) < 0.8f) continue;
                    float off = Vector3.Dot(nrm, p0);
                    int g = -1;
                    for (int k = 0; k < normals.Count && g < 0; k++)
                        if (Vector3.Dot(normals[k], nrm) > 0.9995f && Mathf.Abs(offsets[k] - off) < 0.001f) g = k;
                    if (g < 0) { normals.Add(nrm); offsets.Add(off); areas.Add(0f); members.Add(new List<Vector3>()); g = normals.Count - 1; }
                    areas[g] += area;
                    members[g].Add(p0); members[g].Add(p1); members[g].Add(p2);
                }
            }
            float biggest = 0f;
            foreach (var a in areas) biggest = Mathf.Max(biggest, a);
            int best = -1;
            float bestAlong = float.MinValue;
            for (int k = 0; k < normals.Count; k++)
            {
                if (areas[k] < biggest * 0.25f) continue;
                float along = 0f;
                foreach (var p in members[k]) along += Vector3.Dot(p, facing);
                along /= members[k].Count;
                if (along > bestAlong) { bestAlong = along; best = k; }
            }
            if (best < 0) return false;

            double n = 0, su = 0, sw = 0, suu = 0, suw = 0;
            foreach (var p in members[best])
            {
                double u = upward ? p.z : p.y, w = upward ? p.y : p.z;
                n++; su += u; sw += w; suu += u * u; suw += u * w;
            }
            double den = n * suu - su * su;
            if (n < 3 || System.Math.Abs(den) < 1e-9) return false;
            k1 = (float)((n * suw - su * sw) / den);
            k0 = (float)((sw - k1 * su) / n);
            return true;
        }

        /// <summary>The sheet, imported by the renderer's own rules for a
        /// texture: point filtered, no mips, clamped, uncompressed. Checked
        /// before it is changed — SaveAndReimport re-encodes even when nothing
        /// moved, and an unconditional one on every bake is how the audio
        /// importer once cost every build five minutes.</summary>
        static Texture2D SodaSheetTexture() => PointTexture(SodaSheet);

        static Texture2D PointTexture(string path)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter imp &&
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
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
