using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The LifeSim prop pipeline: bakes the extracted asset-pack heroes
    /// (houses, trailers, restaurants, mid-rise blocks) into runtime-loadable
    /// prefabs under Resources/CityProps, and plants the Emerald Isle beach
    /// town along the stage road.
    ///
    /// The prefabs exist because Charlotte is STREAMED — a tile conjured at
    /// runtime cannot AssetDatabase-load an FBX, so everything a tile might
    /// stand up has to be a Resources prefab with its materials and colliders
    /// already right. The same prefabs are then reused at EDITOR time by the
    /// stage pass and the house scene, so there is exactly one place where a
    /// pack model learns its PSX materials and its collision.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        const string LifeSimArtDir = Root + "/Art/LifeSim";
        const string CityPropsDir = Root + "/Resources/CityProps";

        static readonly (byte kind, string fbx)[] PropSources =
        {
            (CityProps.House,      LifeSimArtDir + "/House/house_simple.fbx"),
            (CityProps.Trailer0,   LifeSimArtDir + "/Trailer/trailer_00.fbx"),
            (CityProps.Trailer1,   LifeSimArtDir + "/Trailer/trailer_02.fbx"),
            (CityProps.Trailer2,   LifeSimArtDir + "/Trailer/trailer_05.fbx"),
            ((byte)(CityProps.Block0 + 0), LifeSimArtDir + "/Pizzeria/city_building_03.fbx"),
            ((byte)(CityProps.Block0 + 1), LifeSimArtDir + "/Pizzeria/city_building_05.fbx"),
            ((byte)(CityProps.Block0 + 2), LifeSimArtDir + "/Pizzeria/city_building_08.fbx"),
            ((byte)(CityProps.Block0 + 3), LifeSimArtDir + "/Pizzeria/city_building_11.fbx"),
            ((byte)(CityProps.Block0 + 4), LifeSimArtDir + "/Pizzeria/city_building_15.fbx"),
            ((byte)(CityProps.Block0 + 5), LifeSimArtDir + "/Pizzeria/city_building_16.fbx"),
            ((byte)(CityProps.Block0 + 6), LifeSimArtDir + "/Pizzeria/city_building_17.fbx"),
            ((byte)(CityProps.Block0 + 7), LifeSimArtDir + "/Pizzeria/city_building_18.fbx"),
            (CityProps.Burger,     LifeSimArtDir + "/Burger/burger_drive.fbx"),
            (CityProps.Pizzeria,   LifeSimArtDir + "/Pizzeria/pizzeria.fbx"),
        };

        [MenuItem("PSX Racing/Bake City Props")]
        public static void BakeCityProps()
        {
            if (!AssetDatabase.IsValidFolder(Root + "/Resources"))
                AssetDatabase.CreateFolder(Root, "Resources");
            if (!AssetDatabase.IsValidFolder(CityPropsDir))
                AssetDatabase.CreateFolder(Root + "/Resources", "CityProps");

            EnsureRoadLayer();
            int baked = 0;
            foreach (var (kind, fbx) in PropSources)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                if (prefab == null)
                {
                    Log("WARN: prop source missing: " + fbx);
                    continue;
                }
                var def = CityProps.Defs[kind];
                string name = System.IO.Path.GetFileNameWithoutExtension(def.res);

                var inst = (GameObject)Object.Instantiate(prefab);
                inst.name = name;
                ConvertToPSXMaterials(inst);
                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                    t.gameObject.isStatic = false;   // streamed tiles move whole objects

                if (kind == CityProps.Burger) DressBurger(inst, def);
                else if (kind == CityProps.Pizzeria) DressPizzeria(inst, def);
                else AddSolidBox(inst, def);

                // Every prop stands on ground that undulates, and the lots seat
                // on their HIGHEST corner — the skirt is what the low corner
                // shows instead of daylight under the floor slab.
                AddSkirt(inst, def.w, def.d);

                string path = CityPropsDir + "/" + name + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(inst, path);
                Object.DestroyImmediate(inst);
                baked++;
            }
            AssetDatabase.SaveAssets();
            Log("City props baked: " + baked + " prefabs -> " + CityPropsDir);
        }

        /// <summary>One solid box over the model's renderer bounds — a house is
        /// a wall to a car whichever porch it hides behind.</summary>
        static void AddSolidBox(GameObject inst, CityProps.Def def)
        {
            var b = RendererBounds(inst);
            if (b.size.sqrMagnitude < 0.01f) return;
            var solid = new GameObject("Solid");
            solid.transform.SetParent(inst.transform, false);
            solid.transform.localPosition = b.center;
            solid.layer = SolidLayer;
            var bc = solid.AddComponent<BoxCollider>();
            // A shave off the plan footprint so a kerbside mailbox or porch
            // step does not widen the wall the car actually hits.
            bc.size = new Vector3(Mathf.Max(1f, b.size.x - 0.6f), b.size.y,
                                  Mathf.Max(1f, b.size.z - 0.6f));
        }

        /// <summary>
        /// The burger lot: solid over the BUILDING only (the drive lane and
        /// parking must stay drivable), an order-window trigger by the menu
        /// board, and the DriveThru brain on the trigger.
        /// </summary>
        static void DressBurger(GameObject inst, CityProps.Def def)
        {
            AddApron(inst, def.w + 5f, def.d + 5f);
            Transform shell = FindDeep(inst.transform, "BurgerPiz");
            var shellBounds = shell != null
                ? RendererBounds(shell.gameObject) : RendererBounds(inst);
            var solid = new GameObject("Solid");
            solid.transform.SetParent(inst.transform, false);
            solid.transform.position = shellBounds.center;
            solid.layer = SolidLayer;
            solid.AddComponent<BoxCollider>().size = shellBounds.size;

            // The freestanding menu board marks the order lane: among the Menu
            // meshes the TALL one stands in the lot; the wide flat one hangs
            // over the counter inside.
            Transform board = null;
            foreach (var t in inst.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Menu")) continue;
                var r = t.GetComponentInChildren<MeshRenderer>();
                if (r == null) continue;
                if (r.bounds.size.y > 2f) { board = t; break; }
            }
            Vector3 at = board != null
                ? new Vector3(board.position.x, shellBounds.min.y, board.position.z)
                : shellBounds.center + new Vector3(shellBounds.extents.x + 4f, -shellBounds.extents.y, 0f);

            AddOrderBay(inst, at + Vector3.up * 1.4f, new Vector3(10f, 3f, 10f),
                        DriveThru.Venue.Burger);
        }

        /// <summary>The pizzeria is a corner shop: no lane, so the order bay is
        /// the kerb — a stopped car anywhere along either face is "parked
        /// outside", which is what curbside pickup is.</summary>
        static void DressPizzeria(GameObject inst, CityProps.Def def)
        {
            AddApron(inst, def.w + 4f, def.d + 8f);
            AddSolidBox(inst, def);
            var b = RendererBounds(inst);
            AddOrderBay(inst, new Vector3(b.center.x, b.min.y + 1.4f, b.center.z),
                        new Vector3(b.size.x + 5f, 3f, b.size.z + 11f),
                        DriveThru.Venue.Pizzeria);
        }

        static void AddOrderBay(GameObject inst, Vector3 worldPos, Vector3 size,
                                DriveThru.Venue venue)
        {
            var bay = new GameObject("OrderBay");
            bay.transform.SetParent(inst.transform, false);
            bay.transform.position = worldPos;
            var bc = bay.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = size;
            bay.AddComponent<DriveThru>().venue = venue;
        }

        /// <summary>Concrete foundation from just above the base line down two
        /// metres. Purely visual — the ground collider is still the ground.</summary>
        static void AddSkirt(GameObject inst, float w, float d)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                LifeSimArtDir + "/House/Textures/ConcreteBare.jpg");
            var mat = PSXMaterialFor(tex, "PropSkirt", new Vector2(4f, 1f), Vector2.zero);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Skirt";
            go.transform.SetParent(inst.transform, false);
            go.transform.localPosition = new Vector3(0f, -1.05f, 0f);
            go.transform.localScale = new Vector3(
                Mathf.Max(1f, w - 0.8f), 2.2f, Mathf.Max(1f, d - 0.8f));
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>Tarmac lot under a restaurant, a whisker proud of the
        /// grass. No collider: 15 mm is beneath the suspension's notice.</summary>
        static void AddApron(GameObject inst, float w, float d)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                LifeSimArtDir + "/House/Textures/Asphalt.jpg");
            var mat = PSXMaterialFor(tex, "PropApron", new Vector2(6f, 9f), Vector2.zero);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Apron";
            go.transform.SetParent(inst.transform, false);
            go.transform.localPosition = new Vector3(0f, -0.045f, 0f);
            go.transform.localScale = new Vector3(w, 0.12f, d);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<MeshRenderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static Transform FindDeep(Transform root, string prefix)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(prefix)) return t;
            return null;
        }

        // ==================================================================
        //  The Emerald Isle beach town
        // ==================================================================
        /// <summary>
        /// Houses and trailers along the stage road, and somewhere to eat: the
        /// island reads as a town instead of a bare spit of scrub. Everything
        /// seats on the stage DEM, keeps out of the corridor the cars use, and
        /// refuses lots that are steep, wet, or on a bridge approach.
        /// </summary>
        static void BuildStageHomes(List<Vector3> pts, Transform parent)
        {
            var root = new GameObject("BeachTown");
            root.transform.SetParent(parent, false);

            var defs = new List<(byte kind, GameObject prefab)>();
            foreach (var (kind, _) in PropSources)
            {
                var def = CityProps.Defs[kind];
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(
                    CityPropsDir + "/" + System.IO.Path.GetFileNameWithoutExtension(def.res) + ".prefab");
                if (p != null) defs.Add((kind, p));
            }
            GameObject Prefab(byte kind)
            {
                foreach (var (k, p) in defs) if (k == kind) return p;
                return null;
            }
            if (defs.Count == 0)
            {
                Log("WARN: no CityProps prefabs — beach town skipped (bake props first)");
                return;
            }

            var rand = new System.Random(41);
            int placed = 0, eats = 0;
            float startM = track.stageStartLineM;
            float finishM = track.FinishIndex * Spacing;

            for (int i = 6; i < pts.Count - 6; i += 12 + rand.Next(0, 10))
            {
                float m = i * Spacing;
                // clear of the staging box and the traps, and off the bridges
                if (Mathf.Abs(m - startM) < 90f || Mathf.Abs(m - finishM) < 60f) continue;
                if (OverBridge(m)) continue;

                int side = rand.Next(2) == 0 ? -1 : 1;
                Vector3 rightv = RightAt(pts, i);
                float off = 14f + (float)rand.NextDouble() * 6f;
                Vector3 at = pts[i] + rightv * (side * off);

                float gy = GroundHeightAt(at.x, at.z);
                // the sound side of Emerald Drive slides underwater fast; a
                // beach house with wet carpet is a skipped lot
                if (track.stage && trackStageWaterY() > -9000f && gy < trackStageWaterY() + 0.6f) continue;
                // reject slopes that would bury a corner or float a porch,
                // then seat on the HIGHEST sample — the skirt hides the rest
                float g2 = GroundHeightAt(at.x + 6f, at.z);
                float g3 = GroundHeightAt(at.x, at.z + 6f);
                if (Mathf.Abs(g2 - gy) > 2.2f || Mathf.Abs(g3 - gy) > 2.2f) continue;
                gy = Mathf.Max(gy, Mathf.Max(g2, g3));

                // the restaurants: one burger box past the traps, one pizzeria
                // mid-island, then houses and trailers for everyone else
                byte kind;
                if (eats == 0 && m > finishM + 80f)
                { kind = CityProps.Burger; eats++; }
                else if (eats == 1 && m > finishM + 700f)
                { kind = CityProps.Pizzeria; eats++; }
                else
                {
                    double r = rand.NextDouble();
                    kind = r < 0.62 ? CityProps.House
                         : r < 0.92 ? (byte)(CityProps.Trailer0 + rand.Next(3))
                         : CityProps.House;
                }

                var prefab = Prefab(kind);
                if (prefab == null) continue;
                var def = CityProps.Defs[kind];

                var go = (GameObject)Object.Instantiate(prefab);
                go.name = prefab.name;
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(at.x, gy - def.sink, at.z);
                // face the road: the lot sits at +right*side, so looking back
                // along -right*side is looking at the tarmac
                Vector3 face = -rightv * side;
                go.transform.rotation = Quaternion.LookRotation(face, Vector3.up)
                                        * Quaternion.Euler(0f, def.yawOffsetDeg, 0f);
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    t.gameObject.isStatic = true;
                placed++;
            }
            Log("Beach town: " + placed + " lots (" + eats + " places to eat)");
        }

        /// <summary>Stage water level, or -9999 when the stage has none. Small
        /// wrapper because the field lives on the catalog def.</summary>
        static float trackStageWaterY() =>
            track != null && track.stageWaterY != 0f ? track.stageWaterY : -9999f;

        /// <summary>Is this distance along the route on a bridge span?</summary>
        static bool OverBridge(float m)
        {
            if (track == null || track.bridges == null) return false;
            foreach (var span in track.bridges)
                if (m >= span.x - 30f && m <= span.y + 30f) return true;
            return false;
        }
    }
}
