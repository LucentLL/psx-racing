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
            // MEASURED IN THE PROP'S OWN SPACE, not in the world's.
            //
            // Renderer.bounds is a world AABB, and a world AABB of a ROTATED
            // house is not the house: at 45 degrees it is half as big again in
            // plan, and the box built from it is then applied back in the
            // prop's own frame, so it stands out past the walls by metres at
            // each end. That is an invisible wall beside the road, and the new
            // barrier-face pass in TrackObstacleAudit named four of them along
            // the Emerald Isle beach town before anybody drove into one.
            //
            // It is also the re-bake this box has been waiting for: the
            // Charlotte skyline is switched OFF (see stageHomes in the city
            // theme) because its baked prefabs carry a Solid box that "spans
            // far more than the model", which is this, from here.
            //
            // The displaced-centre bug the previous version fixed is fixed by
            // construction now: measured in local space, the centre IS a local
            // position.
            var b = LocalRendererBounds(inst);
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
        /// The burger lot: the building and its furniture collided piece by
        /// piece (the drive lane and parking stay drivable — flat pieces fail
        /// the size filter), an order-window trigger by the menu board, and
        /// the DriveThru brain on the trigger.
        ///
        /// It USED to be one box over the building shell, which sealed the
        /// modelled dining room behind an invisible wall. The restaurants are
        /// the two props in the set with real interiors, and "I am unable to
        /// go inside" is the bug one box per building is.
        /// </summary>
        static void DressBurger(GameObject inst, CityProps.Def def)
        {
            WorldKit.HingeDoors(inst);
            WorldKit.AddColliders(inst, SolidLayer);
            AddApron(inst, def.w + 5f, def.d + 5f);
            Transform shell = FindDeep(inst.transform, "BurgerPiz");
            var shellBounds = shell != null
                ? RendererBounds(shell.gameObject) : RendererBounds(inst);

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
        /// outside", which is what curbside pickup is. Collided piece by piece
        /// with its double doors on hinges, for the same reason as the
        /// burger box above: the dining room is modelled, and it is for
        /// walking into.</summary>
        static void DressPizzeria(GameObject inst, CityProps.Def def)
        {
            WorldKit.HingeDoors(inst);
            WorldKit.AddColliders(inst, SolidLayer);
            AddApron(inst, def.w + 4f, def.d + 8f);
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

        /// <summary>
        /// The prop's drawn extent IN ITS OWN SPACE — an oriented box rather
        /// than a world one, so a rotated model gets a collider the shape of
        /// the model instead of the shape of its shadow on the world axes.
        ///
        /// Mesh.bounds rather than Renderer.bounds on purpose: Renderer.bounds
        /// is the world AABB, which is the very thing being avoided, and
        /// Mesh.bounds is serialised so it works on the imported models here,
        /// none of which are readable.
        /// </summary>
        static Bounds LocalRendererBounds(GameObject go)
        {
            bool any = false;
            var acc = new Bounds();
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var lb = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = lb.center + Vector3.Scale(lb.extents,
                        new Vector3((c & 1) == 0 ? -1f : 1f,
                                    (c & 2) == 0 ? -1f : 1f,
                                    (c & 4) == 0 ? -1f : 1f));
                    Vector3 p = go.transform.InverseTransformPoint(
                                    r.transform.TransformPoint(corner));
                    if (!any) { acc = new Bounds(p, Vector3.zero); any = true; }
                    else acc.Encapsulate(p);
                }
            }
            return any ? acc : new Bounds(Vector3.zero, Vector3.zero);
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
            // The same pass dresses a Charlotte street when the theme hands
            // it the city's prop set (theme.stageProps): the towers, the
            // mid-rise blocks and the suburbs' houses, dealt by how far the
            // lot is from Trade & Tryon. Same seating, same clearances.
            bool city = theme.stageProps != null;
            var root = new GameObject(city ? "StreetFront" : "BeachTown");
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
            int placed = 0, eats = 0, sunk = 0, crowded = 0, towers = 0, blocks = 0;
            float startM = track.stageStartLineM;
            float finishM = track.FinishIndex * Spacing;
            float lapM = pts.Count * Spacing;
            bool loop = track.loop;

            // The city's set, bucketed. Towers and blocks are ranges of kind;
            // the block range ends where the food kinds begin (CityProps:
            // "5..12 are the eight mid-rise shells").
            var towerKinds = new List<byte>(); var blockKinds = new List<byte>();
            var suburbKinds = new List<byte>(); var foodKinds = new List<byte>();
            if (city)
                foreach (var k in theme.stageProps)
                {
                    if (k >= CityProps.Tower0 && k < CityProps.Tower0 + CityProps.TowerCount) towerKinds.Add(k);
                    else if (k >= CityProps.Block0 && k < CityProps.Burger) blockKinds.Add(k);
                    else if (CityProps.IsFood(k)) foodKinds.Add(k);
                    else suburbKinds.Add(k);
                }
            Vector2 uptown = StageUptownPoint(out bool hasUptown);
            if (city)
                Log(hasUptown
                    ? $"Street front: uptown at ({uptown.x:0},{uptown.y:0}) in the stage frame, " +
                      $"{Vector2.Distance(uptown, new Vector2(pts[0].x, pts[0].z)):0} m from waypoint 0"
                    : "Street front: no city frame on this bake — suburbs only");

            // Along-the-road occupancy per side, so two lots on one shoulder
            // cannot overlap: the site pitch is 48-84 m and a tower's lot is
            // up to 49 m wide.
            var usedTo = new[] { float.NegativeInfinity, float.NegativeInfinity };

            for (int i = 6; i < pts.Count - 6; i += 12 + rand.Next(0, 10))
            {
                float m = i * Spacing;
                // clear of the staging box and the traps, and off the bridges.
                // On a loop the grid stands BEHIND waypoint 0, which is the
                // far end of the lap, so the distance to the line wraps.
                float fromStart = Mathf.Abs(m - startM);
                if (loop) fromStart = Mathf.Min(fromStart, lapM - fromStart);
                if (fromStart < 90f || (!loop && Mathf.Abs(m - finishM) < 60f)) continue;
                if (OverBridge(m)) continue;

                int side = rand.Next(2) == 0 ? -1 : 1;
                Vector3 rightv = RightAt(pts, i);
                float wobble = 14f + (float)rand.NextDouble() * 6f;

                byte kind;
                if (!city)
                {
                    // the restaurants: one burger box past the traps, one
                    // pizzeria mid-island, then houses and trailers for
                    // everyone else
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
                }
                else
                {
                    float du = hasUptown
                        ? Vector2.Distance(uptown, new Vector2(pts[i].x, pts[i].z)) : float.MaxValue;
                    kind = PickCityKind(rand, du, towerKinds, blockKinds, suburbKinds, foodKinds, ref eats);
                    if (kind == 0) continue;
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

                // ALONG the road: the previous lot on this shoulder must have
                // ended before this one begins.
                float halfAlong = Mathf.Max(def.w, def.d) * 0.5f + 3f;
                int sideIdx = side > 0 ? 1 : 0;
                if (m - halfAlong < usedTo[sideIdx]) { crowded++; continue; }

                Vector3 face = -rightv * side;
                var rot = Quaternion.LookRotation(face, Vector3.up);
                Vector3 fwd = rot * Vector3.forward, rgt = rot * Vector3.right;

                // ACROSS the route: every corner of the lot clear of EVERY arm
                // of it, not only the one it fronts. The 277 belt is a ring
                // whose arms pass within 85 m of each other, and a 49 m tower
                // set back from one arm reaches most of the way to the next.
                // Same test PushClearOfTrack applies to a circuit's buildings.
                bool clearOfRoute = true;
                float wantClear = WallOffsetFor(track) + 3f;
                for (int c = 0; c < 4 && clearOfRoute; c++)
                {
                    Vector3 corner = at + rgt * (((c & 1) == 0 ? -0.5f : 0.5f) * def.w)
                                        + fwd * (((c & 2) == 0 ? -0.5f : 0.5f) * def.d);
                    if (PlanDistanceToPath(pts, corner) < wantClear) clearOfRoute = false;
                }
                if (!clearOfRoute) { crowded++; continue; }

                // Sample the REAL surface under all four corners of the lot and
                // the middle of it. All five must find ground, or the lot is
                // over water or off the edge of the chunked terrain.
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

                // AND NOW ASK THE COLLIDER, not the catalogue.
                //
                // Every clearance above is computed from def.w/def.d — the
                // dimensions the prop was BAKED at. A prefab whose Solid box
                // is bigger than that, or whose pivot is not its centre, lands
                // wherever it lands, and on 2026-09-07 that put invisible
                // building colliders 0.0 m off the centreline of both
                // Charlotte freeways: "invisible walls causing cars to crash
                // in the middle of the road", on a venue that had passed every
                // audit because the sweep excused colliders by name and this
                // one is a building.
                //
                // So the last word belongs to the geometry that will actually
                // hit the car. Anything reaching inside the barrier line is
                // not moved or shrunk — it is DROPPED. A missing building is a
                // bare lot; a building on the road is the game.
                // RE-SEAT THE SOLID BOX ON THE BUILDING IT BELONGS TO.
                //
                // AddSolidBox measures the model's RendererBounds — world
                // space — and assigns that centre as a LOCAL position. Baked
                // at the origin the two agree and nobody noticed; baked
                // anywhere else the invisible box sits displaced by however
                // far the prop stood from (0,0,0), which is how buildings
                // whose walls are politely set back off the verge came to
                // have their colliders lying across both Charlotte freeways
                // ("invisible walls causing cars to crash in the middle of
                // the road"). Fixed at the source too, but every prefab
                // already baked carries it, so it is corrected here on the
                // instance: put the box back over the geometry it is meant to
                // stand for.
                foreach (var box in go.GetComponentsInChildren<BoxCollider>(true))
                {
                    if (box.name != "Solid") continue;
                    var rb = RendererBounds(go);
                    if (rb.size.sqrMagnitude < 0.01f) continue;
                    var owner = box.transform.parent != null ? box.transform.parent : go.transform;
                    box.transform.position = rb.center;
                    box.transform.rotation = owner.rotation;
                    box.center = Vector3.zero;
                }

                // Measured through the collider's OWN TRANSFORM, never through
                // Collider.bounds: bounds are a physics-side cache that Unity
                // does not refresh until the scene syncs, so the first version
                // of this check read every freshly instantiated building at
                // the pose it had before it was moved, found it miles from the
                // road, and passed all three venues unchanged.
                Physics.SyncTransforms();
                float intrude = 0f;
                foreach (var col in go.GetComponentsInChildren<Collider>(true))
                {
                    var ct = col.transform;
                    Vector3 c, e;
                    if (col is BoxCollider box) { c = box.center; e = box.size * 0.5f; }
                    else { var lb = col.bounds; c = ct.InverseTransformPoint(lb.center); e = lb.extents; }
                    // A box that straddles the road has a corner on each side,
                    // so the centre alone would not catch it.
                    for (int cx = -1; cx <= 1; cx += 2)
                        for (int cz = -1; cz <= 1; cz += 2)
                        {
                            Vector3 w = ct.TransformPoint(c + new Vector3(cx * e.x, 0f, cz * e.z));
                            w.y = 0f;
                            intrude = Mathf.Max(intrude, wantClear - PlanDistanceToPath(pts, w));
                        }
                }
                if (intrude > 0f)
                {
                    Object.DestroyImmediate(go);
                    crowded++;
                    continue;
                }

                usedTo[sideIdx] = m + halfAlong;
                if (towerKinds.Contains(kind)) towers++;
                else if (blockKinds.Contains(kind)) blocks++;
                placed++;
            }

            Physics.queriesHitBackfaces = hitBackfaces;
            Log((city ? "Street front: " : "Beach town: ") + placed + " lots (" +
                (city ? towers + " towers, " + blocks + " blocks, " : "") + eats + " places to eat), " +
                sunk + " sites rejected as wet, steep or off-mesh, " + crowded + " as overlapping");
        }

        // ------------------------------------------------------------------
        //  The city's prop set, dealt by distance from uptown
        // ------------------------------------------------------------------
        /// <summary>Towers stand inside this radius of Trade & Tryon (the
        /// city's own CityBuildings uses ~900 m), fading out over
        /// <see cref="CoreFadeM"/> beyond it.</summary>
        const float CoreRadiusM = 900f, CoreFadeM = 400f;
        /// <summary>Mid-rise blocks reach this far out, then fade.</summary>
        const float MidRadiusM = 2600f, MidFadeM = 500f;
        /// <summary>Houses, trailers and the drive-thrus begin here and are
        /// all there is by the end of the fade.</summary>
        const float SuburbFromM = 1800f, SuburbFadeM = 1200f;
        /// <summary>Weight of a mid-rise block against a tower inside the
        /// core: uptown is not all towers.</summary>
        const float BlockInCoreWeight = 0.7f;

        /// <summary>
        /// Which kind of building stands on a lot <paramref name="du"/>
        /// metres from uptown. Three soft bands — towers in the core, blocks
        /// to the edge of it, the suburbs beyond — rolled by weight rather
        /// than cut by radius so the transitions do not read as a fence.
        /// The two restaurants go up once each, on the first suburban lots
        /// the roll hands them. Returns 0 for "leave this lot empty".
        /// </summary>
        static byte PickCityKind(System.Random rand, float du, List<byte> towers, List<byte> blocks,
                                 List<byte> suburb, List<byte> food, ref int eats)
        {
            float wT = towers.Count > 0 ? 1f - Mathf.Clamp01((du - CoreRadiusM) / CoreFadeM) : 0f;
            float wB = blocks.Count > 0 ? (1f - Mathf.Clamp01((du - MidRadiusM) / MidFadeM)) * BlockInCoreWeight : 0f;
            float wS = suburb.Count + food.Count > 0 ? Mathf.Clamp01((du - SuburbFromM) / SuburbFadeM) : 0f;
            // Off the map, or past every band the theme has kinds for: the
            // suburbs are what is left.
            if (wT + wB + wS <= 0f) wS = suburb.Count + food.Count > 0 ? 1f : 0f;
            float total = wT + wB + wS;
            if (total <= 0f) return 0;
            double r = rand.NextDouble() * total;
            if (r < wT) return towers[rand.Next(towers.Count)];
            if (r < wT + wB) return blocks[rand.Next(blocks.Count)];
            if (eats < food.Count) return food[eats++];
            if (suburb.Count == 0) return 0;
            // Houses over trailers, as the beach town does.
            var pick = suburb[rand.Next(suburb.Count)];
            if (pick >= CityProps.Trailer0 && pick <= CityProps.Trailer2 && rand.NextDouble() < 0.5)
                pick = suburb.Contains(CityProps.House) ? CityProps.House : pick;
            return pick;
        }

        /// <summary>
        /// Trade & Tryon in THIS stage's frame, via the registration the bake
        /// wrote (charlotte_city.json's frame and the stage's differ only by
        /// a translation and a tenth of a per cent of scale). False on any
        /// bake outside Charlotte, and the caller treats every lot as
        /// suburban.
        /// </summary>
        static Vector2 StageUptownPoint(out bool has)
        {
            has = false;
            if (track == null || !track.stageInCity) return Vector2.zero;
            var map = CityMap.Get();
            if (map == null) return Vector2.zero;
            has = true;
            return map.uptown / CityMap.LayoutScale - track.stageCityOrigin;
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
                // TERRAIN only — the ground chunks and the decks, which are
                // the concave MeshColliders (the audits' own rule for "a
                // surface"). A guard wall's box or a cut bank's box is a
                // thing that STANDS on the ground, and a lot whose corner
                // finds one seats the whole house on top of it.
                if (!(h.collider is MeshCollider)) continue;
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
