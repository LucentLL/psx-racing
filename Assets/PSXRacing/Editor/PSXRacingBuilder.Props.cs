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
        const string SkyscraperDir = LifeSimArtDir + "/Skyscrapers";

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
            ((byte)(CityProps.Tower0 +  0), SkyscraperDir + "/building_01.1.fbx"),
            ((byte)(CityProps.Tower0 +  1), SkyscraperDir + "/building_01.2.fbx"),
            ((byte)(CityProps.Tower0 +  2), SkyscraperDir + "/building_01.3.fbx"),
            ((byte)(CityProps.Tower0 +  3), SkyscraperDir + "/building_01.4.fbx"),
            ((byte)(CityProps.Tower0 +  4), SkyscraperDir + "/building_02.1.fbx"),
            ((byte)(CityProps.Tower0 +  5), SkyscraperDir + "/building_02.2.fbx"),
            ((byte)(CityProps.Tower0 +  6), SkyscraperDir + "/building_03.1.fbx"),
            ((byte)(CityProps.Tower0 +  7), SkyscraperDir + "/building_03.2.fbx"),
            ((byte)(CityProps.Tower0 +  8), SkyscraperDir + "/building_04.1.fbx"),
            ((byte)(CityProps.Tower0 +  9), SkyscraperDir + "/building_04.2.fbx"),
            ((byte)(CityProps.Tower0 + 10), SkyscraperDir + "/building_05.1.fbx"),
            ((byte)(CityProps.Tower0 + 11), SkyscraperDir + "/building_05.2.fbx"),
            ((byte)(CityProps.Tower0 + 12), SkyscraperDir + "/building_06.1.fbx"),
            ((byte)(CityProps.Tower0 + 13), SkyscraperDir + "/building_06.2.fbx"),
            ((byte)(CityProps.Tower0 + 14), SkyscraperDir + "/building_07.1.fbx"),
            ((byte)(CityProps.Tower0 + 15), SkyscraperDir + "/building_07.2.fbx"),
            ((byte)(CityProps.Tower0 + 16), SkyscraperDir + "/building_08.1.fbx"),
            ((byte)(CityProps.Tower0 + 17), SkyscraperDir + "/building_08.2.fbx"),
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

                var model = (GameObject)Object.Instantiate(prefab);

                // The MODEL is scaled; the prefab ROOT is not. Everything added
                // below — solid box, order bay, foundation skirt — is authored
                // in real metres, so it must not inherit the correction, and
                // the world-space bounds it measures already carry it.
                GameObject inst;
                if (def.Scale != 1f)
                {
                    inst = new GameObject(name);
                    model.transform.SetParent(inst.transform, false);
                    model.transform.localScale = Vector3.one * def.Scale;
                }
                else { inst = model; inst.name = name; }

                ConvertToPSXMaterials(inst);
                ForcePackTexture(inst, fbx);
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

            // The cargo rides along with the props for the same reason it is a
            // prefab at all: a race scene cannot AssetDatabase-load an FBX, and
            // a bake nobody remembers to run is a delivery with no pizza in it.
            PizzaCargoBaker.Bake();
        }

        /// <summary>
        /// Put the skyscraper pack's own texture on a tower.
        ///
        /// The pack ships ONE texture per family — building_01.png dressing
        /// building_01.1 through .4 — in a "textures" subfolder, and its FBXs
        /// name the material "building_01" with no embedded texture. Unity's
        /// importer does not make that connection, so every tower came out of
        /// ConvertToPSXMaterials as flat white: a downtown of blank slabs,
        /// which looks like a shader problem and is a lookup problem.
        ///
        /// Derived from the FBX filename rather than declared per row, because
        /// the pack's own naming IS the mapping — building_04.2.fbx wears
        /// building_04.png and there is nothing to decide. A row outside the
        /// skyscraper folder is left exactly as the model importer found it.
        /// </summary>
        static void ForcePackTexture(GameObject inst, string fbx)
        {
            if (!fbx.StartsWith(SkyscraperDir)) return;
            string family = System.IO.Path.GetFileName(fbx);
            int dot = family.IndexOf('.');
            if (dot > 0) family = family.Substring(0, dot);
            string texPath = SkyscraperDir + "/textures/" + family + ".png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null) { Log("WARN: no pack texture at " + texPath); return; }

            var mat = PSXMaterialFor(tex, family, Vector2.one, Vector2.zero);
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
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

            // The ground is already BUILT and collided by the time this runs,
            // so seat every lot on the surface the player will actually stand
            // on rather than on the height function that described it. Those
            // two disagreed — the stage ground is chunked, masked and pinned to
            // the road corridor after the DEM is sampled — and the difference
            // is what left half the beach town buried to the eaves.
            Physics.SyncTransforms();
            bool hitBackfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;

            var rand = new System.Random(41);
            int placed = 0, eats = 0, sunk = 0;
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
                float wobble = 14f + (float)rand.NextDouble() * 6f;

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

                // Set back by the LOT, not by the lot's centre.
                //
                // A fixed 14-20 m offset is a statement about where the middle
                // of a building goes, and these buildings are not the same
                // size: the drive-thru is 36 m deep, so its centre at 14 m put
                // its near wall four metres past the CENTRELINE — an invisible
                // block of concrete standing across the road, which is exactly
                // how the obstacle audit found it. The lot faces the road, so
                // its depth is what reaches toward it; the wider dimension is
                // taken anyway, because a collider baked from a model's bounds
                // does not have to agree with the def about which way round it
                // is.
                float clear = WallOffsetFor(track) + 3f + Mathf.Max(def.w, def.d) * 0.5f;
                float off = Mathf.Max(wobble, clear);
                Vector3 at = pts[i] + rightv * (side * off);

                // Sample the REAL surface under all four corners of the lot and
                // the middle of it. All five must find ground, or the lot is
                // over water or off the edge of the chunked terrain.
                Vector3 face = -rightv * side;
                var rot = Quaternion.LookRotation(face, Vector3.up);
                Vector3 fwd = rot * Vector3.forward, rgt = rot * Vector3.right;
                float lo = float.MaxValue, hi = float.MinValue;
                bool ok = true;
                for (int c = 0; c < 5 && ok; c++)
                {
                    float fx = c == 4 ? 0f : ((c & 1) == 0 ? -0.5f : 0.5f) * def.w;
                    float fz = c == 4 ? 0f : ((c & 2) == 0 ? -0.5f : 0.5f) * def.d;
                    Vector3 probe = at + rgt * fx + fwd * fz;
                    if (SurfaceY(probe, out float sy)) { lo = Mathf.Min(lo, sy); hi = Mathf.Max(hi, sy); }
                    else ok = false;
                }
                if (!ok) { sunk++; continue; }
                // A lot that falls away by more than the skirt can cover is a
                // house on stilts at one corner; leave that ground empty.
                if (hi - lo > 1.8f) { sunk++; continue; }
                if (trackStageWaterY() > -9000f && lo < trackStageWaterY() + 0.5f) { sunk++; continue; }

                var go = (GameObject)Object.Instantiate(prefab);
                go.name = prefab.name;
                go.transform.SetParent(root.transform, false);
                // Seat on the HIGHEST corner: a model cannot stretch its walls
                // down into a bank, and the baked foundation skirt is what the
                // low corner shows instead of daylight.
                go.transform.position = new Vector3(at.x, hi - def.sink, at.z);
                // face the road: the lot sits at +right*side, so looking back
                // along -right*side is looking at the tarmac
                go.transform.rotation = rot * Quaternion.Euler(0f, def.yawOffsetDeg, 0f);
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    t.gameObject.isStatic = true;
                placed++;
            }

            Physics.queriesHitBackfaces = hitBackfaces;
            Log("Beach town: " + placed + " lots (" + eats + " places to eat), " +
                sunk + " sites rejected as wet, steep or off-mesh");
        }

        /// <summary>
        /// World Y of the ground under a point, off the terrain that has
        /// actually been built. Drops from well overhead so it lands on the top
        /// face, and ignores the ROAD layer — a lot whose corner overhangs the
        /// tarmac must seat on the earth beside it, not on the carriageway.
        /// </summary>
        static bool SurfaceY(Vector3 at, out float y)
        {
            y = 0f;
            var hits = Physics.RaycastAll(new Vector3(at.x, at.y + 400f, at.z),
                                          Vector3.down, 900f);
            bool found = false;
            float best = float.MinValue;
            foreach (var h in hits)
            {
                if (h.collider.gameObject.layer == RoadLayer) continue;
                if (h.point.y > best) { best = h.point.y; found = true; }
            }
            if (found) y = best;
            return found;
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
