using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE PLACE ALONG A STAGE'S ROAD, from its world file
    /// (tools/roads/bake_world.mjs): first US 64/74A from Lake Lure through
    /// Chimney Rock village to Bat Cave as it stood before Hurricane Helene.
    ///
    ///   * BUILDINGS on their OpenStreetMap footprints of 2024-09-20, seated on
    ///     the lidar ground: walls in the city's own house-atlas materials
    ///     (siding, boards, cedar, stucco), windows, storefronts, porches with
    ///     balconies on the village's street fronts, metal / shingle / clay
    ///     roofs. Signs are generic words, never a real business's name.
    ///   * SIDE ROADS: every road that meets the route, its real mouth and
    ///     width, laid on the ground for 14 m and closed for the race there by a
    ///     Type III barricade. The edge line is covered across the mouth.
    ///   * THE PARK ROAD from Main Street over its river bridge to where the
    ///     Chimney Rock stage starts, on that stage's own line and heights.
    ///   * The Flowering Bridge, the lakeside boardwalk, sidewalks and
    ///     crosswalks downtown and in the village, wooden utility poles with
    ///     their wires, lantern street lamps, and the signs.
    ///
    /// Switches: PSX_LURE_TOWN=0 (no buildings, furniture or signs),
    /// PSX_LURE_MOUTHS=0 (no side-road mouths, park road or barricades).
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        [Serializable] class WorldSpeed { public float st; public int mph; }
        [Serializable] class WorldSigns { public string[] names, shops; public float[] uv; }

        static Dictionary<string, Rect> signUv;
        static string[] signShops;
        static Transform worldRoot;

        // ---------------------------------------------------------- materials
        const string LureTex = Root + "/Art/LakeLure/Tex/";
        static Material TownMat(string key)
        {
            switch (key)
            {
                case "siding": return MakeMat(MeshPrefix + "TownSiding", LureTex + "siding_white.png");
                case "planks": return MakeMat(MeshPrefix + "TownPlanks", LureTex + "wood_planks.png");
                case "cedar": return MakeMat(MeshPrefix + "TownCedar", LureTex + "wood_cedar.png");
                case "darkwood": return MakeMat(MeshPrefix + "TownDarkWood", LureTex + "wood_dark.png");
                case "stucco": return MakeMat(MeshPrefix + "TownStucco", LureTex + "stucco_white.png");
                case "brick": return MakeMat(MeshPrefix + "TownBrick", Root + "/Art/Buildings/Textures/BrickSmallPainted.jpg", tint: new Color(0.80f, 0.55f, 0.45f));
                case "stone": return MakeMat(MeshPrefix + "TownStone", Root + "/Art/Roadside/rock_pt_2.png", tint: new Color(0.85f, 0.82f, 0.78f));
                case "window": return MakeMat(MeshPrefix + "TownWindow", LureTex + "window.png");
                case "store": return MakeMat(MeshPrefix + "TownStore", LureTex + "storefront.png");
                case "shingle": return MakeMat(MeshPrefix + "TownShingle", LureTex + "shingle_dark.png");
                case "metalgreen": return MakeMat(MeshPrefix + "TownMetalGreen", LureTex + "metal_roof.png", tint: new Color(0.42f, 0.62f, 0.45f));
                case "metalred": return MakeMat(MeshPrefix + "TownMetalRed", LureTex + "metal_roof.png", tint: new Color(0.70f, 0.30f, 0.24f));
                case "metalgrey": return MakeMat(MeshPrefix + "TownMetalGrey", LureTex + "metal_roof.png", tint: new Color(0.86f, 0.87f, 0.88f));
                case "tilered": return MakeMat(MeshPrefix + "TownTileRed", LureTex + "tile_roof_red.png");
                case "trim": return MakeMat(MeshPrefix + "TownTrim", Root + "/Art/Buildings/Textures/ConcreteBare.png", tint: new Color(1.05f, 1.05f, 1.02f));
                case "concrete": return MakeMat(MeshPrefix + "TownConcrete", Root + "/Art/City/Pack/concrete_pt_4_walk.png");
                case "asphalt": return MakeMat(MeshPrefix + "TownAsphalt", Root + "/Art/City/city_road_junction_asphalt_old.png");
                case "gravel": return MakeMat(MeshPrefix + "TownGravel", Root + "/Art/City/Pack/dirt_pt_7_city.png");
                case "paint": return MakeMat(MeshPrefix + "TownPaint", Root + "/Art/Buildings/Textures/ConcreteBare.png", tint: new Color(1.15f, 1.15f, 1.12f));
                case "black": return MakeMat(MeshPrefix + "TownBlack", LureTex + "metal_roof.png", tint: new Color(0.10f, 0.10f, 0.11f));
                case "galv": return MakeMat(MeshPrefix + "TownGalv", LureTex + "metal_roof.png", tint: new Color(0.62f, 0.64f, 0.66f));
                case "lantern": return MakeMat(MeshPrefix + "TownLantern", Root + "/Art/Buildings/Textures/ConcreteBare.png", tint: new Color(1.25f, 1.08f, 0.70f));
                case "grass": return MakeMat(MeshPrefix + "TownGrass", Root + "/Art/City/Pack/grass_7_city.png");
                case "sand": return MakeMat(MeshPrefix + "TownSand", Root + "/Art/Beach/sand_texture_21.png");
                case "yellow": return MakeMat(MeshPrefix + "TownYellow", Root + "/Art/Buildings/Textures/ConcreteBare.png", tint: new Color(1.25f, 0.92f, 0.22f));
                case "mulch": return MakeMat(MeshPrefix + "TownMulch", Root + "/Art/City/Pack/mulch_dirt_6_city.png");
                case "signs": return MakeMat(MeshPrefix + "TownSigns", Root + "/Art/LakeLure/lure_signs.png", cutoff: 0.5f, twoSided: false);
            }
            throw new Exception("no town material " + key);
        }

        /// <summary>A mesh under construction with one submesh per material key.</summary>
        class TownBuf
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly Dictionary<string, List<int>> sub = new Dictionary<string, List<int>>();
            List<int> T(string k) { if (!sub.TryGetValue(k, out var l)) sub[k] = l = new List<int>(); return l; }
            /// <summary>A quad a-b-c-d (counter-clockwise seen from its front).</summary>
            public void Quad(string k, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
                var t = T(k); t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2);
            }
            public void Tri(string k, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
            {
                int i = v.Count; v.Add(a); v.Add(b); v.Add(c); uv.Add(ua); uv.Add(ub); uv.Add(uc);
                var t = T(k); t.Add(i); t.Add(i + 2); t.Add(i + 1);
            }
            /// <summary>A box from a centre, its axes and half sizes; every face.</summary>
            /// <summary>The same quad wound the other way.</summary>
            public void QuadR(string k, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud) => Quad(k, a, d, c, b, ua, ud, uc, ub);
            public void Box(string k, Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, float hx, float hy, float hz, float uvScale = 1f)
            {
                Vector3 X = ax * hx, Y = ay * hy, Z = az * hz;
                Vector3[] p = { c - X - Y - Z, c + X - Y - Z, c + X + Y - Z, c - X + Y - Z, c - X - Y + Z, c + X - Y + Z, c + X + Y + Z, c - X + Y + Z };
                float sx = 2 * hx * uvScale, sy = 2 * hy * uvScale, sz = 2 * hz * uvScale;
                QuadR(k, p[0], p[3], p[2], p[1], new Vector2(0, 0), new Vector2(0, sy), new Vector2(sx, sy), new Vector2(sx, 0));
                QuadR(k, p[5], p[6], p[7], p[4], new Vector2(0, 0), new Vector2(0, sy), new Vector2(sx, sy), new Vector2(sx, 0));
                QuadR(k, p[4], p[7], p[3], p[0], new Vector2(0, 0), new Vector2(0, sy), new Vector2(sz, sy), new Vector2(sz, 0));
                QuadR(k, p[1], p[2], p[6], p[5], new Vector2(0, 0), new Vector2(0, sy), new Vector2(sz, sy), new Vector2(sz, 0));
                QuadR(k, p[3], p[7], p[6], p[2], new Vector2(0, 0), new Vector2(0, sz), new Vector2(sx, sz), new Vector2(sx, 0));
                QuadR(k, p[4], p[0], p[1], p[5], new Vector2(0, 0), new Vector2(0, sz), new Vector2(sx, sz), new Vector2(sx, 0));
            }
            public int Count => v.Count;
        }

        static GameObject TownEmit(TownBuf b, string name, Transform parent, int layer = 0, bool collide = false)
        {
            if (b.Count == 0) return null;
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(b.v); mesh.SetUVs(0, b.uv);
            var keys = new List<string>(b.sub.Keys);
            mesh.subMeshCount = keys.Count;
            for (int k = 0; k < keys.Count; k++) mesh.SetTriangles(b.sub[keys[k]], k, false);
            mesh.RecalculateBounds();
            SaveMesh(mesh, "Lure" + name);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mats = new Material[keys.Count];
            for (int k = 0; k < keys.Count; k++) mats[k] = TownMat(keys[k]);
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            go.isStatic = true;
            if (collide) { var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; }
            return go;
        }

        static GameObject TownCollider(List<Vector3> v, List<int> t, string name, Transform parent, int layer)
        {
            if (t.Count == 0) return null;
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetTriangles(t, 0);
            SaveMesh(mesh, "Lure" + name + "Coll");
            var go = new GameObject(name + "Coll");
            go.transform.SetParent(parent, false);
            go.layer = layer;
            var mc = go.AddComponent<MeshCollider>(); mc.convex = false; mc.sharedMesh = mesh;
            return go;
        }

        static void LoadSignUv()
        {
            signUv = new Dictionary<string, Rect>();
            string p = ProjectRootPath(Root + "/Art/LakeLure/lure_signs.json");
            if (!File.Exists(p)) return;
            var s = JsonUtility.FromJson<WorldSigns>(File.ReadAllText(p));
            for (int i = 0; i < s.names.Length; i++) signUv[s.names[i]] = new Rect(s.uv[4 * i], s.uv[4 * i + 1], s.uv[4 * i + 2], s.uv[4 * i + 3]);
            signShops = s.shops;
        }

        /// <summary>A sign face (or any atlas face) standing upright at c, facing
        /// <paramref name="facing"/>, w x h metres, with an optional back.</summary>
        static void SignFace(TownBuf b, string face, Vector3 c, Vector3 facing, float w, float h, bool back = true)
        {
            if (!signUv.TryGetValue(face, out Rect r)) return;
            Vector3 f = new Vector3(facing.x, 0, facing.z).normalized, right = Vector3.Cross(Vector3.up, f);
            // seen from the front (looking along -f), right is to the viewer's left; build so the text reads correctly
            Vector3 L = c + right * (w * 0.5f), R = c - right * (w * 0.5f), up = Vector3.up * h;
            Vector3 off = f * 0.012f;
            b.Quad("signs", L + off, R + off, R + up + off, L + up + off,
                   new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax));
            if (back) b.Quad("galv", R - off, L - off, L + up - off, R + up - off, Vector2.zero, new Vector2(w, 0), new Vector2(w, h), new Vector2(0, h));
        }

        // ----------------------------------------------------------- drape
        static readonly RaycastHit[] drapeHits = new RaycastHit[16];
        /// <summary>The highest drawn surface under (x, z) that is ground, road or
        /// shoulder (not a wall, not ours), else the lattice.</summary>
        static float DrapeY(float x, float z)
        {
            int mask = ~((1 << SolidLayer) | (1 << FoliageLayer));
            int n = Physics.RaycastNonAlloc(new Vector3(x, 2000f, z), Vector3.down, drapeHits, 4000f, mask, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var t = drapeHits[i].collider.transform;
                if (worldRoot != null && t.IsChildOf(worldRoot)) continue;
                best = Mathf.Max(best, drapeHits[i].point.y);
            }
            return float.IsNegativeInfinity(best) ? WorldGroundY(x, z) : best;
        }

        static int StationAt(float m) => Mathf.Clamp(Mathf.RoundToInt(m / Spacing), 0, stageWp.Count - 1);
        static bool WalledNear(int i, float side, int reach)
        {
            int s = SideIx(side);
            for (int k = i - reach; k <= i + reach; k++)
                if (k >= 0 && k < stageWp.Count && StageRowWalled(s, k)) return true;
            return false;
        }

        // ================================================================ town
        static void BuildWorldTown(List<Vector3> pts, Transform parent)
        {
            worldRoot = parent;
            Physics.SyncTransforms();
            LoadSignUv();
            bool town = WorldSwitch("PSX_LURE_TOWN"), mouths = WorldSwitch("PSX_LURE_MOUTHS");
            // another stage's frame (the Park Road stage's start): the place, not this road's own furniture
            bool own = world.stage == StagePrefix;
            if (mouths) { if (own) BuildWorldMouths(pts, parent); BuildWorldParkRoad(parent); }
            if (town)
            {
                BuildWorldStreets(parent);
                BuildWorldBuildings(pts, parent);
                if (own) { BuildWorldWalksAndBridges(pts, parent); BuildWorldFurniture(pts, parent); }
            }
        }

        // -------------------------------------------------------- buildings
        static int Hash(float a, float b) => unchecked((int)(a * 73.856093f) * 19349663 ^ (int)(b * 83.492791f) * 83492791) & 0x7fffffff;

        static void BuildWorldBuildings(List<Vector3> pts, Transform parent)
        {
            if (world.buildings == null) return;
            var bufs = new Dictionary<int, TownBuf>();
            var collV = new Dictionary<int, List<Vector3>>(); var collT = new Dictionary<int, List<int>>();
            int built = 0, porches = 0, signs = 0, skipped = 0, hips = 0, chimneys = 0;
            var byKind = new Dictionary<string, int>();
            // round 2 (owner's video): real forms - a hipped roof on every pitched footprint the gable cannot cover,
            // steeper house roofs, corner boards, chimneys, the inn's long two-storey veranda (PSX_LURE_FORMS=0: round 1)
            bool forms = WorldSwitch("PSX_LURE_FORMS");
            float halfW = RoadWidth * 0.5f;
            int bi = -1;
            foreach (var b in world.buildings)
            {
                bi++;
                // round 3: a building matched to the owner's video wears what the video shows (PSX_LURE_VIDEOLOOK)
                var look = world.stage == StagePrefix ? VideoLook(bi) : null;
                var ring = new List<Vector3>(WorldPts(b.xz));
                if (ring.Count < 3 || b.area < 10f || b.area > 4000f) { skipped++; continue; }
                // CLOCKWISE from above (x east, z north): outward is to the left of travel along the ring
                float sa = 0f; for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++) sa += (ring[j].x - ring[i].x) * (ring[j].z + ring[i].z);
                if (sa > 0f) ring.Reverse();
                // never in the road: a footprint that reaches into the tarmac or shoulder is left out (and said)
                bool inRoad = false;
                foreach (var p in ring) if (StageCorridor(p.x, p.z, halfW + 1.6f, out _)) { inRoad = true; break; }
                if (inRoad) { skipped++; continue; }
                int chunk = Mathf.FloorToInt(b.st / 400f);
                if (!bufs.TryGetValue(chunk, out var buf)) { bufs[chunk] = buf = new TownBuf(); collV[chunk] = new List<Vector3>(); collT[chunk] = new List<int>(); }
                int h = Hash(b.xz[0], b.xz[1]);
                // ground: the lower of the lidar and the built ground under it; walls run 0.6 m into it
                float gMin = b.g0, gMax = b.g1;
                foreach (var p in ring) { float g = WorldGroundY(p.x, p.z); gMin = Mathf.Min(gMin, g); gMax = Mathf.Max(gMax, g); }
                string kind = b.kind ?? "yes", zone = b.zone ?? "lake", role = b.role ?? "";
                bool shop = (zone == "village" || zone == "downtown" || zone == "inlet" || (zone == "batcave" && b.dist < 40f)) && kind != "house" && kind != "shed" && kind != "garage" && kind != "garages";
                bool canopy = kind == "roof";
                int floors = b.levels > 0 ? b.levels
                    : role == "inn" ? 3 : role == "church" ? 1 : kind == "hotel" ? 3
                    : kind == "shed" || kind == "garage" || kind == "garages" || kind == "static_caravan" || canopy ? 1
                    : zone == "village" ? (h % 10 < 7 ? 2 : 1) : shop ? (h % 10 < 4 ? 2 : 1) : (h % 10 < 5 ? 2 : 1);
                if (look != null && look.floors > 0) floors = look.floors;
                float storey = shop ? 3.4f : kind == "shed" || kind == "garage" ? 2.7f : role == "church" ? 5.5f : 3.0f;
                float baseY = gMin - 0.6f;
                // ON POSTS where the ground falls away under it (the village's river-side shops stood
                // over the bank on posts): the floor at the high side, posts and a beam below
                bool stilts = (gMax - gMin) > 1.8f && zone != "lake" && zone != "downtown";
                float floorY = stilts ? gMax + 0.2f : gMin + 0.25f + Mathf.Min(1.2f, (gMax - gMin) * 0.35f);
                // a street front stands at the street's level (the bench lowered the land before it)
                float streetY = stageWp[StationAt(b.st)].y;
                if (world.stage == StagePrefix && WorldOnStreet(b.st) && b.dist < 32f && Mathf.Abs(b.g0 - streetY) < 3.5f)
                {
                    floorY = stilts ? Mathf.Max(streetY + 0.15f, Mathf.Min(floorY, streetY + 0.6f)) : streetY + 0.15f;
                    baseY = Mathf.Min(baseY, floorY - 0.8f);
                }
                float eave = floorY + floors * storey;
                // the look
                string wall, roofMat; float pitchDeg;
                if (role == "inn") { wall = "stucco"; roofMat = "tilered"; pitchDeg = 26f; }
                else if (role == "arcade" || role == "postoffice") { wall = "brick"; roofMat = "metalgrey"; pitchDeg = 0f; }
                else if (role == "church") { wall = "siding"; roofMat = "shingle"; pitchDeg = 38f; }
                else if (zone == "village" || zone == "inlet")
                {
                    int k = h % 20; wall = k < 7 ? "planks" : k < 12 ? "siding" : k < 15 ? "cedar" : k < 17 ? "darkwood" : k < 19 ? "stucco" : "brick";
                    int r = (h / 20) % 20; roofMat = r < 8 ? "metalgreen" : r < 13 ? "metalred" : r < 16 ? "metalgrey" : "shingle"; pitchDeg = roofMat == "shingle" ? 30f : 22f;
                }
                else if (zone == "downtown")
                {
                    int k = h % 20; wall = k < 8 ? "stucco" : k < 14 ? "siding" : k < 17 ? "brick" : "planks";
                    int r = (h / 20) % 20; roofMat = r < 6 ? "tilered" : r < 14 ? "shingle" : "metalgrey"; pitchDeg = roofMat == "metalgrey" ? 18f : 28f;
                }
                else
                {
                    int k = h % 20; wall = k < 10 ? "siding" : k < 14 ? "cedar" : k < 17 ? "planks" : "darkwood";
                    int r = (h / 20) % 20; roofMat = r < 13 ? "shingle" : r < 16 ? "metalgreen" : r < 18 ? "metalred" : "metalgrey"; pitchDeg = roofMat == "shingle" ? 32f : 24f;
                }
                if (forms && !shop && role.Length == 0 && pitchDeg > 0f) pitchDeg = Mathf.Max(pitchDeg, 33f);
                if (look != null) { if (look.wall != null) wall = look.wall; if (look.roof != null) roofMat = look.roof; if (look.pitch >= 0f) pitchDeg = look.pitch; }
                // the oriented box (for the roof) and how much of it the footprint fills
                Vector3 cen = Vector3.zero; foreach (var p in ring) cen += p; cen /= ring.Count;
                float sxx = 0, szz = 0, sxz = 0; foreach (var p in ring) { float dx = p.x - cen.x, dz = p.z - cen.z; sxx += dx * dx; szz += dz * dz; sxz += dx * dz; }
                float ang = 0.5f * Mathf.Atan2(2f * sxz, sxx - szz);
                Vector3 ax = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)), az = new Vector3(-ax.z, 0, ax.x);
                float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                foreach (var p in ring) { float u = Vector3.Dot(p - cen, ax), w = Vector3.Dot(p - cen, az); x0 = Mathf.Min(x0, u); x1 = Mathf.Max(x1, u); z0 = Mathf.Min(z0, w); z1 = Mathf.Max(z1, w); }
                float fill = b.area / Mathf.Max(1f, (x1 - x0) * (z1 - z0));
                bool gable = !canopy && pitchDeg > 0f && fill >= 0.72f && b.area < 1400f;
                // toward the road
                Vector3 roadAt = stageWp[StationAt(b.st)];
                // the way to the road it fronts, from the bake (frame-free: right in another stage's frame too)
                Vector3 toRoad = (b.fx * b.fx + b.fz * b.fz) > 0.25f ? new Vector3(b.fx, 0, b.fz).normalized
                               : new Vector3(roadAt.x - cen.x, 0, roadAt.z - cen.z).normalized;
                int front = -1; float frontDot = 0.35f, frontScore = 0f;
                var cv = collV[chunk]; var ct = collT[chunk];
                // ----- walls
                for (int i = 0; i < ring.Count && !canopy; i++)
                {
                    Vector3 a = ring[i], c = ring[(i + 1) % ring.Count];
                    Vector3 e = new Vector3(c.x - a.x, 0, c.z - a.z); float len = e.magnitude; if (len < 0.3f) continue;
                    Vector3 dir = e / len, outN = new Vector3(-dir.z, 0, dir.x);   // clockwise ring: outward is to the left of travel
                    float dot = Vector3.Dot(outN, toRoad);
                    if (forms) { if (dot > 0.45f && len >= 4f && dot * len > frontScore) { frontScore = dot * len; front = i; } }
                    else if (dot > frontDot && len >= 4f) { frontDot = dot; front = i; }
                    float wu = wall == "siding" ? 4f : 2.5f;
                    if (forms && (wall == "siding" || wall == "planks" || wall == "cedar"))
                        buf.Box("trim", new Vector3(a.x, (floorY + eave) * 0.5f, a.z) + outN * 0.02f, dir, Vector3.up, outN, 0.1f, (eave - floorY) * 0.5f, 0.06f);
                    float top = gable ? eave : eave + (pitchDeg > 0f || canopy ? 0f : 0.7f);
                    Vector3 A0 = new Vector3(a.x, baseY, a.z), C0 = new Vector3(c.x, baseY, c.z), A1 = new Vector3(a.x, top, a.z), C1 = new Vector3(c.x, top, c.z);
                    // stone footing to the floor, then the wall - or, over a fall, posts and a beam
                    float ga = WorldGroundY(a.x, a.z), gc = WorldGroundY(c.x, c.z);
                    if (stilts && Mathf.Min(ga, gc) < floorY - 1.0f)
                    {
                        int np = Mathf.Max(1, Mathf.RoundToInt(len / 2.4f));
                        for (int k = 0; k <= np; k++)
                        {
                            Vector3 q = Vector3.Lerp(a, c, k / (float)np) - outN * 0.12f;
                            float g = WorldGroundY(q.x, q.z) - 0.4f;
                            if (floorY - g > 0.3f) buf.Box("darkwood", new Vector3(q.x, (g + floorY) * 0.5f, q.z), dir, Vector3.up, outN, 0.11f, (floorY - g) * 0.5f, 0.11f);
                        }
                        Vector3 bm = (a + c) * 0.5f - outN * 0.1f;
                        buf.Box("darkwood", new Vector3(bm.x, floorY - 0.15f, bm.z), dir, Vector3.up, outN, len * 0.5f, 0.15f, 0.1f);
                        A0 = new Vector3(a.x, floorY - 0.3f, a.z); C0 = new Vector3(c.x, floorY - 0.3f, c.z);
                    }
                    else
                    {
                        float fb = stilts ? Mathf.Min(ga, gc) - 0.6f : baseY;
                        A0 = new Vector3(a.x, fb, a.z); C0 = new Vector3(c.x, fb, c.z);
                        buf.Quad("stone", A0, new Vector3(a.x, floorY, a.z), new Vector3(c.x, floorY, c.z), C0,
                                 new Vector2(0, 0), new Vector2(0, (floorY - fb) / 2f), new Vector2(len / 2f, (floorY - fb) / 2f), new Vector2(len / 2f, 0));
                    }
                    buf.Quad(wall, new Vector3(a.x, floorY, a.z), A1, C1, new Vector3(c.x, floorY, c.z),
                             new Vector2(0, 0), new Vector2(0, (top - floorY) / 2.5f), new Vector2(len / wu, (top - floorY) / 2.5f), new Vector2(len / wu, 0));
                    int ci = cv.Count; cv.Add(A0); cv.Add(A1); cv.Add(C1); cv.Add(C0); ct.Add(ci); ct.Add(ci + 2); ct.Add(ci + 1); ct.Add(ci); ct.Add(ci + 3); ct.Add(ci + 2);
                    // windows (and the storefront on a shop's street side)
                    bool street = shop && dot > 0.55f && len >= 4f && !(forms && role == "inn");
                    for (int f = 0; f < floors; f++)
                    {
                        float fy = floorY + f * storey;
                        if (f == 0 && street)
                        {
                            float sw = len - 1.0f, s0 = 0.5f;
                            Vector3 p0 = a + dir * s0 + outN * 0.03f, p1 = a + dir * (s0 + sw) + outN * 0.03f;
                            buf.Quad("store", new Vector3(p0.x, fy + 0.15f, p0.z), new Vector3(p0.x, fy + 2.8f, p0.z), new Vector3(p1.x, fy + 2.8f, p1.z), new Vector3(p1.x, fy + 0.15f, p1.z),
                                     new Vector2(0, 0), new Vector2(0, 1), new Vector2(Mathf.Max(1f, Mathf.Round(sw / 5f)), 1), new Vector2(Mathf.Max(1f, Mathf.Round(sw / 5f)), 0));
                            continue;
                        }
                        int nw = Mathf.FloorToInt((len - 0.8f) / 2.9f);
                        for (int k = 0; k < nw; k++)
                        {
                            float s = (len - nw * 2.9f) * 0.5f + 2.9f * k + 0.95f;
                            Vector3 p0 = a + dir * s + outN * 0.04f, p1 = a + dir * (s + 1.0f) + outN * 0.04f;
                            float y0 = fy + 0.9f, y1 = fy + 2.25f;
                            buf.Quad("window", new Vector3(p0.x, y0, p0.z), new Vector3(p0.x, y1, p0.z), new Vector3(p1.x, y1, p1.z), new Vector3(p1.x, y0, p1.z),
                                     new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
                        }
                    }
                }
                if (stilts)
                {
                    var ccwF = new List<Vector3>(ring); ccwF.Reverse();
                    var triF = EarClip(ccwF);
                    float fy = floorY - 0.3f;
                    for (int k = 0; k + 2 < triF.Count; k += 3)
                    {
                        Vector3 p0 = ccwF[triF[k]], p1 = ccwF[triF[k + 1]], p2 = ccwF[triF[k + 2]];
                        // facing DOWN: the counter-clockwise triangle given the other way round
                        buf.Tri("planks", new Vector3(p0.x, fy, p0.z), new Vector3(p2.x, fy, p2.z), new Vector3(p1.x, fy, p1.z),
                                new Vector2(p0.x / 2f, p0.z / 2f), new Vector2(p2.x / 2f, p2.z / 2f), new Vector2(p1.x / 2f, p1.z / 2f));
                    }
                }
                if (!canopy)
                {
                    var ccwC = new List<Vector3>(ring); ccwC.Reverse();
                    var triC = EarClip(ccwC);
                    int cb = cv.Count;
                    foreach (var p in ccwC) cv.Add(new Vector3(p.x, eave, p.z));
                    for (int k = 0; k + 2 < triC.Count; k += 3) { ct.Add(cb + triC[k]); ct.Add(cb + triC[k + 2]); ct.Add(cb + triC[k + 1]); }
                }
                // ----- roof
                float ov = 0.4f;
                Vector3 Pt(float u, float w, float y) => cen + ax * u + az * w + Vector3.up * y;
                if (canopy)
                {
                    // a shed on posts (the marina's covered slips, a carport)
                    float ry = gMin + 3.4f;
                    var q0 = Pt(x0, z0, ry); var q1 = Pt(x0, z1, ry); var q2 = Pt(x1, z1, ry + 0.3f); var q3 = Pt(x1, z0, ry + 0.3f);
                    buf.Quad(roofMat, q0, q1, q2, q3, Vector2.zero, new Vector2(0, (z1 - z0) / 3f), new Vector2((x1 - x0) / 3f, (z1 - z0) / 3f), new Vector2((x1 - x0) / 3f, 0));
                    buf.Quad(roofMat, q3, q2, q1, q0, Vector2.zero, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
                    for (float u = x0; u <= x1 + 0.01f; u += Mathf.Max(2.5f, (x1 - x0) / Mathf.Max(1, Mathf.Round((x1 - x0) / 4f))))
                        foreach (float w in new[] { z0 + 0.2f, z1 - 0.2f })
                            buf.Box("darkwood", Pt(u, w, (gMin - 0.5f + ry) * 0.5f), Vector3.right, Vector3.up, Vector3.forward, 0.09f, (ry - gMin + 0.5f) * 0.5f, 0.09f);
                }
                else if (gable)
                {
                    float rise = Mathf.Tan(pitchDeg * Mathf.Deg2Rad) * (z1 - z0) * 0.5f;
                    float zm = (z0 + z1) * 0.5f, ridge = eave + rise;
                    float ru = (x1 - x0 + 2 * ov), rw = Mathf.Sqrt(rise * rise + (zm - z0 + ov) * (zm - z0 + ov));
                    var e0 = Pt(x0 - ov, z0 - ov, eave - ov * rise / ((z1 - z0) * 0.5f)); var e1 = Pt(x1 + ov, z0 - ov, eave - ov * rise / ((z1 - z0) * 0.5f));
                    var e2 = Pt(x1 + ov, z1 + ov, eave - ov * rise / ((z1 - z0) * 0.5f)); var e3 = Pt(x0 - ov, z1 + ov, eave - ov * rise / ((z1 - z0) * 0.5f));
                    var r0 = Pt(x0 - ov, zm, ridge); var r1 = Pt(x1 + ov, zm, ridge);
                    float tile = roofMat == "shingle" || roofMat == "tilered" ? 3f : 2.5f;
                    buf.QuadR(roofMat, e0, r0, r1, e1, Vector2.zero, new Vector2(0, rw / tile), new Vector2(ru / tile, rw / tile), new Vector2(ru / tile, 0));
                    buf.QuadR(roofMat, e2, r1, r0, e3, Vector2.zero, new Vector2(0, rw / tile), new Vector2(ru / tile, rw / tile), new Vector2(ru / tile, 0));
                    // undersides of the overhang (seen from below)
                    buf.QuadR("trim", e1, r1, r0, e0, Vector2.zero, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
                    buf.QuadR("trim", e3, r0, r1, e2, Vector2.zero, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
                    if (forms && !shop && role.Length == 0 && h % 2 == 0 && x1 - x0 > 6f)
                    {
                        float cy0 = eave - 0.4f, cy1 = ridge + 0.9f;
                        buf.Box("brick", Pt(x0 + 1.1f, zm + (z1 - z0) * 0.12f, (cy0 + cy1) * 0.5f), ax, Vector3.up, az, 0.42f, (cy1 - cy0) * 0.5f, 0.42f);
                        chimneys++;
                    }
                    // gable ends: the wall material up to the ridge, on the box's short sides
                    foreach (float u in new[] { x0, x1 })
                    {
                        var g0 = Pt(u, z0, eave); var g1 = Pt(u, z1, eave); var g2 = Pt(u, zm, ridge);
                        if (u == x0) buf.Tri(wall, g1, g0, g2, Vector2.zero, new Vector2((z1 - z0) / 2.5f, 0), new Vector2((z1 - z0) / 5f, rise / 2.5f));
                        else buf.Tri(wall, g0, g1, g2, Vector2.zero, new Vector2((z1 - z0) / 2.5f, 0), new Vector2((z1 - z0) / 5f, rise / 2.5f));
                    }
                }
                else if (forms && pitchDeg > 0f && HipRoof(buf, ring, eave, pitchDeg, roofMat, Mathf.Min(x1 - x0, z1 - z0))) hips++;
                else
                {
                    // flat or low-slope: a cap over the footprint (ear-clipped) behind a parapet
                    var ccw = new List<Vector3>(ring); ccw.Reverse();
                    var tri = EarClip(ccw);
                    float cy = eave + 0.55f;
                    for (int k = 0; k + 2 < tri.Count; k += 3)
                    {
                        Vector3 p0 = ccw[tri[k]], p1 = ccw[tri[k + 1]], p2 = ccw[tri[k + 2]];
                        buf.Tri("concrete", new Vector3(p0.x, cy, p0.z), new Vector3(p1.x, cy, p1.z), new Vector3(p2.x, cy, p2.z),
                                new Vector2(p0.x / 4f, p0.z / 4f), new Vector2(p1.x / 4f, p1.z / 4f), new Vector2(p2.x / 4f, p2.z / 4f));
                    }
                }
                int frontEnd = front;
                if (forms && !canopy) front = PorchFront(ring, toRoad, out frontEnd);
                // ----- porch (and the balcony over it) on the street front
                if (!canopy && front >= 0 && !(look != null && look.noPorch) && (shop || zone == "village" || zone == "batcave" || (zone != "gorge" && h % 3 == 0)) && b.dist < 70f)
                {
                    Vector3 a = ring[front], c = ring[(frontEnd + 1) % ring.Count];
                    Vector3 e = new Vector3(c.x - a.x, 0, c.z - a.z); float len = e.magnitude; Vector3 dir = e / len, outN = new Vector3(-dir.z, 0, dir.x);                    bool veranda = forms && role == "inn";
                    float depth = veranda ? 3.0f : 2.2f;
                    // stay off the shoulder: the porch's front no nearer the road than the edge + 1.6 m
                    for (int tries = 0; tries < 4; tries++)
                    {
                        Vector3 m = (a + c) * 0.5f + outN * depth;
                        if (!StageCorridor(m.x, m.z, halfW + 1.6f, out _) && !StageCorridor(a.x + outN.x * depth, a.z + outN.z * depth, halfW + 1.6f, out _)
                            && !StageCorridor(c.x + outN.x * depth, c.z + outN.z * depth, halfW + 1.6f, out _)) break;
                        depth -= 0.6f;
                    }
                    if (depth >= 1.3f)
                    {
                        float py = floorY + (veranda ? 2f : 1f) * storey - 0.2f;
                        Vector3 pa = a + dir * 0.3f, pc = c - dir * 0.3f;
                        Vector3 fa = pa + outN * depth, fc = pc + outN * depth;
                        string pr = roofMat == "tilered" ? "tilered" : roofMat == "shingle" ? "shingle" : roofMat;
                        buf.Quad(pr, new Vector3(pa.x, py + 0.35f, pa.z), new Vector3(pc.x, py + 0.35f, pc.z), new Vector3(fc.x, py, fc.z), new Vector3(fa.x, py, fa.z),
                                 Vector2.zero, new Vector2(len / 2.5f, 0), new Vector2(len / 2.5f, depth / 2.5f), new Vector2(0, depth / 2.5f));
                        buf.Quad("trim", new Vector3(fa.x, py, fa.z), new Vector3(fc.x, py, fc.z), new Vector3(pc.x, py + 0.35f, pc.z), new Vector3(pa.x, py + 0.35f, pa.z),
                                 Vector2.zero, new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
                        // a porch floor of boards
                        float dy = floorY - 0.02f;
                        buf.QuadR("planks", new Vector3(pa.x, dy, pa.z), new Vector3(fa.x, dy, fa.z), new Vector3(fc.x, dy, fc.z), new Vector3(pc.x, dy, pc.z),
                                 Vector2.zero, new Vector2(0, depth / 2f), new Vector2(len / 2f, depth / 2f), new Vector2(len / 2f, 0));
                        if (veranda)
                        {
                            // the gallery: its floor (seen from above and from under) and its rail
                            float gy = floorY + storey - 0.02f;
                            Vector3 ga = new Vector3(pa.x, gy, pa.z), gfa = new Vector3(fa.x, gy, fa.z), gfc = new Vector3(fc.x, gy, fc.z), gc = new Vector3(pc.x, gy, pc.z);
                            buf.QuadR("planks", ga, gfa, gfc, gc, Vector2.zero, new Vector2(0, depth / 2f), new Vector2(len / 2f, depth / 2f), new Vector2(len / 2f, 0));
                            buf.Quad("trim", ga, gfa, gfc, gc, Vector2.zero, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
                            buf.Box("trim", Vector3.Lerp(fa, fc, 0.5f) + Vector3.up * (gy + 1.0f) - outN * 0.1f, dir, Vector3.up, outN, len * 0.5f - 0.3f, 0.05f, 0.05f);
                            for (float s = 0f; s <= len - 0.6f; s += 0.6f)
                                buf.Box("trim", fa + dir * s - outN * 0.1f + Vector3.up * (gy + 0.5f), dir, Vector3.up, outN, 0.025f, 0.5f, 0.025f);
                        }
                        int np = Mathf.Max(2, Mathf.RoundToInt((len - 0.6f) / 2.6f) + 1);
                        bool stonePiers = zone == "village" && h % 4 == 0;
                        for (int k = 0; k < np; k++)
                        {
                            Vector3 p = Vector3.Lerp(fa, fc, k / (float)(np - 1)) - outN * 0.15f;
                            float g = WorldGroundY(p.x, p.z);
                            if (stonePiers) buf.Box("stone", new Vector3(p.x, (g - 0.3f + floorY + 0.9f) * 0.5f, p.z), dir, Vector3.up, outN, 0.22f, (floorY + 0.9f - g + 0.3f) * 0.5f, 0.22f, 0.5f);
                            buf.Box(stonePiers ? "darkwood" : "trim", new Vector3(p.x, (Mathf.Min(g, floorY) - 0.2f + py) * 0.5f, p.z), dir, Vector3.up, outN, 0.08f, (py - Mathf.Min(g, floorY) + 0.2f) * 0.5f, 0.08f);
                        }
                        // the balcony rail on the porch roof of a two-storey front
                        if (floors >= 2)
                        {
                            float by = py + 0.35f;
                            buf.Box("trim", Vector3.Lerp(fa, fc, 0.5f) + Vector3.up * (by + 1.0f) - outN * 0.1f, dir, Vector3.up, outN, len * 0.5f - 0.3f, 0.05f, 0.05f);
                            for (float s = 0f; s <= len - 0.6f; s += 0.6f)
                            {
                                Vector3 p = fa + dir * s - outN * 0.1f;
                                buf.Box("trim", p + Vector3.up * (by + 0.5f), dir, Vector3.up, outN, 0.025f, 0.5f, 0.025f);
                            }
                        }
                        porches++;
                        // the shop's board on the porch fascia, facing the road
                        if (shop && signShops != null && signShops.Length > 0)
                        {
                            string face = "shop" + (h % signShops.Length).ToString("00");
                            if (role == "inn") face = "shop" + Array.IndexOf(signShops, "INN").ToString("00");
                            Vector3 sc = Vector3.Lerp(fa, fc, 0.5f) + outN * 0.02f + Vector3.up * (py - 0.65f);
                            SignFace(buf, face, sc, outN, Mathf.Min(3.6f, len * 0.6f), 0.62f, back: false);
                            signs++;
                        }
                    }
                }
                built++;
                byKind[role.Length > 0 ? role : kind] = (byKind.TryGetValue(role.Length > 0 ? role : kind, out int nk) ? nk : 0) + 1;
            }
            foreach (var kv in bufs)
            {
                TownEmit(kv.Value, "Buildings_" + kv.Key, parent);
                TownCollider(collV[kv.Key], collT[kv.Key], "Buildings_" + kv.Key, parent, SolidLayer);
            }
            var kinds = new List<string>(); foreach (var kv in byKind) kinds.Add(kv.Key + " " + kv.Value);
            Log($"Stage world buildings: {built} built on their 2024 footprints ({string.Join(", ", kinds)}), {porches} porches, {signs} shop boards; {skipped} left out (too small, too big, or reaching into the road)" +
                (forms ? $"; forms: {hips} hipped roofs, {chimneys} chimneys." : "."));
        }

        /// <summary>Ear clipping of a simple counter-clockwise ring (x, z).</summary>
        static List<int> EarClip(List<Vector3> ring)
        {
            var idx = new List<int>(); for (int i = 0; i < ring.Count; i++) idx.Add(i);
            var o = new List<int>();
            int guard = ring.Count * ring.Count;
            while (idx.Count > 3 && guard-- > 0)
            {
                bool cut = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int ia = idx[(k + idx.Count - 1) % idx.Count], ib = idx[k], ic = idx[(k + 1) % idx.Count];
                    Vector3 a = ring[ia], b = ring[ib], c = ring[ic];
                    float cross = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
                    if (cross <= 1e-6f) continue;
                    bool inside = false;
                    foreach (int j in idx)
                    {
                        if (j == ia || j == ib || j == ic) continue;
                        Vector3 p = ring[j];
                        float d1 = (b.x - a.x) * (p.z - a.z) - (b.z - a.z) * (p.x - a.x), d2 = (c.x - b.x) * (p.z - b.z) - (c.z - b.z) * (p.x - b.x), d3 = (a.x - c.x) * (p.z - c.z) - (a.z - c.z) * (p.x - c.x);
                        if (d1 > 0 && d2 > 0 && d3 > 0) { inside = true; break; }
                    }
                    if (inside) continue;
                    o.Add(ia); o.Add(ib); o.Add(ic);
                    idx.RemoveAt(k); cut = true; break;
                }
                if (!cut) break;
            }
            if (idx.Count == 3) { o.Add(idx[0]); o.Add(idx[1]); o.Add(idx[2]); }
            return o;
        }

        // ------------------------------------------------------ side roads
        /// <summary>Every road that meets the route: its mouth flared onto the
        /// tarmac edge (covering the edge line across it), laid on the ground
        /// for <see cref="MouthLenM"/> and closed there by a Type III barricade.</summary>
        const float MouthLenM = 24f, MouthClosedOutM = 13.5f;

        static void BuildWorldMouths(List<Vector3> pts, Transform parent)
        {
            if (world.roads == null) return;
            float halfW = RoadWidth * 0.5f;
            var surf = new TownBuf(); var furn = new TownBuf();
            int built = 0, walled = 0, shortOnes = 0, closures = 0, facedOut = 0;
            float parkSt = world.parkRoad != null ? world.parkRoad.mouthSt : -1e9f;
            foreach (var r in world.roads)
            {
                // the park road gets its own pass (bridge and all)
                if (world.parkRoad != null && Mathf.Abs(r.st - parkSt) < 6f && r.cls == "service") continue;
                if (r.xz == null || r.xz.Length < 6) continue;
                float side = -r.side;             // the bake's +1 is LEFT; the builder's +1 is the right-hand side
                int st = StationAt(r.st);
                if (r.st < track.stageStartLineM - 30f && r.cls != "secondary") { }
                if (WalledNear(st, side, Mathf.CeilToInt(r.w * 0.5f / Spacing) + 1)) { walled++; continue; }
                var line = WorldPts(r.xz);
                // where it leaves the tarmac (0.45 m inside the edge, over the edge line)
                var P = new List<Vector3>(); var S = new List<float>();
                float acc = 0f; bool started = false; float s0 = 0f;
                for (int i = 0; i < line.Length; i++)
                {
                    if (i > 0) acc += Vector2.Distance(new Vector2(line[i].x, line[i].z), new Vector2(line[i - 1].x, line[i - 1].z));
                    StageCorridor(line[i].x, line[i].z, 80f, out RoadFoot f);
                    if (!started && f.d >= halfW - 0.45f)
                    {
                        started = true;
                        // back up to the exact crossing of the 0.45 m line
                        if (i > 0) { StageCorridor(line[i - 1].x, line[i - 1].z, 80f, out RoadFoot fp); float t = Mathf.InverseLerp(fp.d, f.d, halfW - 0.45f); var q = Vector3.Lerp(line[i - 1], line[i], t); P.Add(q); s0 = acc - (1 - t) * Vector2.Distance(new Vector2(line[i].x, line[i].z), new Vector2(line[i - 1].x, line[i - 1].z)); S.Add(s0); }
                    }
                    if (started) { P.Add(line[i]); S.Add(acc); if (acc - s0 > MouthLenM) break; }
                }
                if (P.Count < 3 || S[S.Count - 1] - s0 < 6f) { shortOnes++; continue; }
                // rows every metre
                var rows = new List<(Vector3 c, Vector3 n, float d)>();
                for (float d = 0f; d <= Mathf.Min(MouthLenM, S[S.Count - 1] - s0); d += 1f)
                {
                    float s = s0 + d; int k = 1; while (k < S.Count - 1 && S[k] < s) k++;
                    float t = Mathf.InverseLerp(S[k - 1], S[k], s);
                    Vector3 c = Vector3.Lerp(P[k - 1], P[k], t), tan = (P[k] - P[k - 1]); tan.y = 0; tan.Normalize();
                    rows.Add((c, new Vector3(-tan.z, 0, tan.x), d));
                }
                const int across = 6;
                int v0 = surf.Count;
                var colV = new List<Vector3>(); var colT = new List<int>();
                // the stub ends where it is closed: the first row standing MouthClosedOutM past the tarmac edge
                int closeRow = -1;
                for (int ri = 0; ri < rows.Count; ri++)
                {
                    StageCorridor(rows[ri].c.x, rows[ri].c.z, 80f, out RoadFoot fr);
                    if (fr.d >= halfW + MouthClosedOutM) { closeRow = ri; break; }
                }
                if (closeRow > 0) rows.RemoveRange(closeRow + 2, Mathf.Max(0, rows.Count - closeRow - 2));
                else if (rows.Count > 16) rows.RemoveRange(16, rows.Count - 16);
                for (int ri = 0; ri < rows.Count; ri++)
                {
                    var (c, n, d) = rows[ri];
                    float flare = d < 5f ? 3.5f * (1f - d / 5f) * (1f - d / 5f) : 0f;
                    float hw = r.w * 0.5f + flare;
                    for (int k = 0; k <= across; k++)
                    {
                        float u = -1f + 2f * k / across;
                        Vector3 p = c + n * (u * hw);
                        // never back over the lanes: a flare corner is pushed out to the 0.45 m line
                        StageCorridor(p.x, p.z, 80f, out RoadFoot f);
                        if (f.d < halfW - 0.45f)
                        {
                            Vector3 fp = Vector3.Lerp(stageWp[f.s0], stageWp[f.s1], f.t);
                            Vector3 away = new Vector3(p.x - fp.x, 0, p.z - fp.z); if (away.sqrMagnitude < 1e-6f) away = n; away.Normalize();
                            p = new Vector3(fp.x, 0, fp.z) + away * (halfW - 0.45f);
                        }
                        float y = DrapeY(p.x, p.z) + (f.d < halfW ? 0.012f : 0.02f);
                        p.y = y;
                        surf.v.Add(p); surf.uv.Add(new Vector2(p.x / 6f, p.z / 6f));
                        colV.Add(p);
                    }
                    if (ri > 0)
                        for (int k = 0; k < across; k++)
                        {
                            int a = v0 + (ri - 1) * (across + 1) + k, b = a + 1, cc = a + across + 1, dd = cc + 1;
                            if (!surf.sub.TryGetValue("asphalt", out var l)) surf.sub["asphalt"] = l = new List<int>();
                            l.Add(a); l.Add(dd); l.Add(cc); l.Add(a); l.Add(b); l.Add(dd);
                            int ca = (ri - 1) * (across + 1) + k;
                            colT.Add(ca); colT.Add(ca + across + 2); colT.Add(ca + across + 1); colT.Add(ca); colT.Add(ca + 1); colT.Add(ca + across + 2);
                        }
                }
                // A FACE ACROSS THE MOUTH (the road's own cut bank, a ditch's lip): no mouth there. The stage
                // builder plans its roadside without the side roads; a stub laid over a rock face is not a road.
                bool faced = false;
                for (int ri = 1; ri < rows.Count && !faced; ri++)
                    for (int k = 0; k <= across; k++)
                    {
                        float y0 = colV[(ri - 1) * (across + 1) + k].y, y1 = colV[ri * (across + 1) + k].y;
                        if (Mathf.Abs(y1 - y0) > 0.45f) { faced = true; break; }
                        if (k > 0 && Mathf.Abs(colV[ri * (across + 1) + k].y - colV[ri * (across + 1) + k - 1].y) > 0.6f) { faced = true; break; }
                    }
                if (faced)
                {
                    // take the stub back out of the buffer
                    int drop = surf.v.Count - v0;
                    surf.v.RemoveRange(v0, drop); surf.uv.RemoveRange(v0, drop);
                    if (surf.sub.TryGetValue("asphalt", out var tl)) tl.RemoveAll(ix => ix >= v0);
                    facedOut++;
                    continue;
                }
                // the closure, if the stub reached it (no collider under the stub: it is laid on the ground a wheel meets)
                if (closeRow > 0)
                {
                    var (c, n, _) = rows[closeRow];
                    float y = DrapeY(c.x, c.z);
                    Vector3 fwd = new Vector3(n.z, 0, -n.x);
                    BuildBarricade(furn, parent, new Vector3(c.x, y, c.z), n, fwd, Mathf.Min(r.w - 0.4f, 6.5f), built);
                    closures++;
                }
                built++;
            }
            TownEmit(surf, "Mouths", parent);
            TownEmit(furn, "MouthClosures", parent);
            Log($"Stage world side roads: {built} mouths laid ({closures} closed by barricades {MouthClosedOutM:0.0} m past the tarmac edge), {walled} left out where the road's own barrier runs across the mouth, " +
                $"{facedOut} where a cut bank or a ditch lip runs across it, {shortOnes} too short to lay.");
        }

        /// <summary>A Type III barricade: two legs, three striped rails, ROAD
        /// CLOSED on top; one box collider for the lot.</summary>
        static void BuildBarricade(TownBuf b, Transform parent, Vector3 at, Vector3 across, Vector3 fwd, float width, int id)
        {
            float hw = width * 0.5f;
            foreach (float s in new[] { -hw + 0.15f, hw - 0.15f })
                b.Box("paint", at + across * s + Vector3.up * 0.8f, across, Vector3.up, fwd, 0.04f, 0.8f, 0.04f);
            foreach (float h in new[] { 0.45f, 0.85f, 1.25f })
            {
                Vector3 c = at + Vector3.up * h;
                SignFace(b, "barricade", c - Vector3.up * 0.11f - fwd * 0.03f, -fwd, width, 0.22f, back: false);
                SignFace(b, "barricade", c - Vector3.up * 0.11f + fwd * 0.03f, fwd, width, 0.22f, back: false);
            }
            SignFace(b, "closed", at + Vector3.up * 1.5f - fwd * 0.04f, -fwd, 1.2f, 0.56f);
            var go = new GameObject("Closure_" + id);
            go.transform.SetParent(parent, false);
            go.layer = SolidLayer;
            go.transform.position = at + Vector3.up * 0.9f;
            go.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            var bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(width, 1.8f, 0.3f);
        }

        // ------------------------------------------------------- park road
        static void BuildWorldParkRoad(Transform parent)
        {
            var pr = world.parkRoad;
            if (pr == null || pr.leadXZ == null || pr.leadXZ.Length < 4) return;
            var lead = WorldPts(pr.leadXZ);
            for (int i = 0; i < lead.Length; i++) lead[i].y = pr.leadY[i];
            // the Park Road stage's own line and heights onward, to 150 m past its start line
            var on = WorldPts(pr.xz);
            for (int i = 0; i < on.Length; i++) on[i].y = pr.y[i];
            var line = new List<Vector3>(lead);
            float upto = pr.startLineM + 150f;
            bool own = world.stage == StagePrefix;
            if (own) for (int i = 1; i < on.Length && i * pr.spacing <= upto; i++) line.Add(on[i]);
            else
            {
                // the Park Road stage draws its own road from its waypoint 0 (and its end pad 6 m back over the bridge)
                int cut = line.Count;
                for (int i = 0; i < line.Count; i++) if (StageCorridor(line[i].x, line[i].z, 40f, out RoadFoot fo) && fo.station < 0.5f && Vector3.Distance(line[i], stageWp[0]) < 6.5f) { cut = i; break; }
                line.RemoveRange(cut, line.Count - cut);
                if (line.Count < 4) return;
            }
            float halfW = RoadWidth * 0.5f, w = 6.1f;           // the park road is built 6.1 m wide in its own stage
            var surf = new TownBuf(); var bridge = new TownBuf();
            var colV = new List<Vector3>(); var colT = new List<int>();
            var S = new List<float> { 0f };
            for (int i = 1; i < line.Count; i++) S.Add(S[i - 1] + Vector2.Distance(new Vector2(line[i].x, line[i].z), new Vector2(line[i - 1].x, line[i - 1].z)));
            int v0 = 0; int rowsMade = 0; bool started = false;
            var deckL = new List<Vector3>(); var deckR = new List<Vector3>();
            for (int i = 0; i < line.Count; i++)
            {
                Vector3 a = line[Mathf.Max(0, i - 1)], b = line[Mathf.Min(line.Count - 1, i + 1)];
                Vector3 t = b - a; t.y = 0; t.Normalize(); Vector3 n = new Vector3(-t.z, 0, t.x);
                StageCorridor(line[i].x, line[i].z, 80f, out RoadFoot f);
                if (!started) { if (own ? f.d < halfW - 0.45f : S[i] < 3.0f) continue; started = true; v0 = surf.Count; }
                bool deck = S[i] >= pr.deckFrom - 0.5f && S[i] <= pr.deckTo + 0.5f;
                float flare = S[i] < 6f ? 3f * (1f - S[i] / 6f) * (1f - S[i] / 6f) : 0f;
                float hw = w * 0.5f + flare;
                for (int k = 0; k <= 4; k++)
                {
                    float u = -1f + k * 0.5f;
                    Vector3 p = line[i] + n * (u * hw);
                    float y = deck ? line[i].y + 0.02f : (S[i] < 20f ? DrapeY(p.x, p.z) + 0.02f : Mathf.Max(line[i].y, DrapeY(p.x, p.z) + 0.02f));
                    p.y = y;
                    surf.v.Add(p); surf.uv.Add(new Vector2(p.x / 6f, p.z / 6f)); colV.Add(p);
                }
                if (rowsMade > 0)
                    for (int k = 0; k < 4; k++)
                    {
                        int q = v0 + (rowsMade - 1) * 5 + k;
                        if (!surf.sub.TryGetValue("asphalt", out var l)) surf.sub["asphalt"] = l = new List<int>();
                        l.Add(q); l.Add(q + 6); l.Add(q + 5); l.Add(q); l.Add(q + 1); l.Add(q + 6);
                        int cq = (rowsMade - 1) * 5 + k; colT.Add(cq); colT.Add(cq + 6); colT.Add(cq + 5); colT.Add(cq); colT.Add(cq + 1); colT.Add(cq + 6);
                    }
                if (deck) { deckL.Add(line[i] + n * (hw + 0.25f) + Vector3.up * line[i].y * 0f); deckR.Add(line[i] - n * (hw + 0.25f)); }
                rowsMade++;
            }
            TownEmit(surf, "ParkRoad", parent, RoadLayer);
            TownCollider(colV, colT, "ParkRoad", parent, RoadLayer);
            // the river bridge: a concrete deck edge, parapets both sides, two piers
            int nd = 0;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                if (!(S[i] >= pr.deckFrom - 0.5f && S[i + 1] <= pr.deckTo + 0.5f)) continue;
                Vector3 a = line[i], b = line[i + 1];
                if (!own && Vector3.Distance((a + b) * 0.5f, stageWp[0]) < 20f) continue;
                Vector3 t = b - a; t.y = 0; float L = t.magnitude; t /= Mathf.Max(0.01f, L); Vector3 n = new Vector3(-t.z, 0, t.x);
                foreach (float sgn in new[] { -1f, 1f })
                {
                    Vector3 pa = a + n * sgn * (w * 0.5f + 0.25f), pb = b + n * sgn * (w * 0.5f + 0.25f);
                    // parapet 0.85 m over the deck, 0.3 m thick; the slab's edge 0.9 m under it
                    Vector3 m = (pa + pb) * 0.5f; float y = (a.y + b.y) * 0.5f;
                    bridge.Box("trim", new Vector3(m.x, y + 0.42f, m.z), t, Vector3.up, n, L * 0.5f + 0.02f, 0.45f, 0.15f, 0.5f);
                    bridge.Box("trim", new Vector3(m.x, y - 0.45f, m.z), t, Vector3.up, n, L * 0.5f + 0.02f, 0.45f, 0.18f, 0.5f);
                    var pg = new GameObject("ParkBridgeParapet_" + nd++);
                    pg.transform.SetParent(parent, false); pg.layer = SolidLayer;
                    pg.transform.position = new Vector3(m.x, y + 0.42f, m.z); pg.transform.rotation = Quaternion.LookRotation(t, Vector3.up);
                    var bc = pg.AddComponent<BoxCollider>(); bc.size = new Vector3(0.3f, 0.9f, L + 0.04f);
                }
                // the slab's underside
                Vector3 l0 = a + n * (w * 0.5f + 0.4f), r0 = a - n * (w * 0.5f + 0.4f), l1 = b + n * (w * 0.5f + 0.4f), r1 = b - n * (w * 0.5f + 0.4f);
                float ya = a.y - 0.9f, yb = b.y - 0.9f;
                bridge.Quad("trim", new Vector3(l0.x, ya, l0.z), new Vector3(l1.x, yb, l1.z), new Vector3(r1.x, yb, r1.z), new Vector3(r0.x, ya, r0.z), Vector2.zero, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
            }
            // two piers at the thirds
            foreach (float frac in new[] { 0.33f, 0.67f })
            {
                float s = Mathf.Lerp(pr.deckFrom, pr.deckTo, frac); int k = 1; while (k < S.Count - 1 && S[k] < s) k++;
                Vector3 c = Vector3.Lerp(line[k - 1], line[k], Mathf.InverseLerp(S[k - 1], S[k], s));
                Vector3 t = line[k] - line[k - 1]; t.y = 0; t.Normalize(); Vector3 n = new Vector3(-t.z, 0, t.x);
                float g = WorldGroundY(c.x, c.z) - 1f, top = c.y - 0.9f;
                if (top - g > 0.5f) bridge.Box("trim", new Vector3(c.x, (g + top) * 0.5f, c.z), n, Vector3.up, t, w * 0.5f, (top - g) * 0.5f, 0.45f, 0.5f);
            }
            TownEmit(bridge, "ParkBridge", parent);
            // closed for the race at the mouth, before the bridge
            var furn = new TownBuf();
            int ci = 1;
            while (ci < S.Count - 1) { StageCorridor(line[ci].x, line[ci].z, 120f, out RoadFoot fc); if (S[ci] >= 13f && fc.d >= halfW + MouthClosedOutM) break; ci++; }
            Vector3 cc = line[ci]; Vector3 tt = line[ci] - line[ci - 1]; tt.y = 0; tt.Normalize();
            BuildBarricade(furn, parent, new Vector3(cc.x, DrapeY(cc.x, cc.z), cc.z), new Vector3(-tt.z, 0, tt.x), tt, w - 0.6f, 900);
            TownEmit(furn, "ParkClosure", parent);
            Log($"Stage world park road: {S[S.Count - 1]:0} m from Main Street on the Park Road stage's own line, its river bridge {pr.deckFrom:0}-{pr.deckTo:0} m ({pr.deckTo - pr.deckFrom:0} m), closed at the mouth.");
        }

        /// <summary>Streets the world file carries whole (Main Street, in the Park
        /// Road stage): the road's own heights, laid on the ground, a collider
        /// on the Road layer.</summary>
        static void BuildWorldStreets(Transform parent)
        {
            if (world.streets == null) return;
            int n = 0;
            foreach (var st in world.streets)
            {
                if (st.xz == null || st.xz.Length < 4) continue;
                var line = WorldPts(st.xz);
                var surf = new TownBuf(); var colV = new List<Vector3>(); var colT = new List<int>();
                for (int i = 0; i < line.Length; i++)
                {
                    Vector3 a = line[Mathf.Max(0, i - 1)], b = line[Mathf.Min(line.Length - 1, i + 1)];
                    Vector3 t = b - a; t.y = 0; t.Normalize(); Vector3 nn = new Vector3(-t.z, 0, t.x);
                    for (int k = 0; k <= 4; k++)
                    {
                        Vector3 p = line[i] + nn * ((-1f + 0.5f * k) * st.w * 0.5f);
                        p.y = Mathf.Max(st.y[i], DrapeY(p.x, p.z)) + 0.02f;
                        surf.v.Add(p); surf.uv.Add(new Vector2(p.x / 6f, p.z / 6f)); colV.Add(p);
                    }
                    if (i > 0)
                        for (int k = 0; k < 4; k++)
                        {
                            int q = (i - 1) * 5 + k;
                            if (!surf.sub.TryGetValue("asphalt", out var l)) surf.sub["asphalt"] = l = new List<int>();
                            l.Add(q); l.Add(q + 6); l.Add(q + 5); l.Add(q); l.Add(q + 1); l.Add(q + 6);
                            colT.Add(q); colT.Add(q + 6); colT.Add(q + 5); colT.Add(q); colT.Add(q + 1); colT.Add(q + 6);
                        }
                }
                TownEmit(surf, "Street_" + n, parent, RoadLayer);
                TownCollider(colV, colT, "Street_" + n, parent, RoadLayer);
                if (WorldSwitch("PSX_LURE_MAINST")) StreetPaintAndWalks(st, line, surf.v, parent, n);
                n++;
            }
            Log($"Stage world streets: {n} drawn whole (Main Street through the village).");
        }

        // ------------------------------------- walks, boardwalk, the Flowering Bridge
        static void BuildWorldWalksAndBridges(List<Vector3> pts, Transform parent)
        {
            var buf = new TownBuf();
            int walks = 0, decks = 0;
            float halfW = RoadWidth * 0.5f;
            // sidewalks on the village's and downtown's street fronts: concrete, flush on the shoulder, a pale
            // curb line at the verge strip's edge (flush: the stage keeps no step beside its tarmac)
            foreach (var (from, to) in new[] { (5100f, 6000f), (6950f, 8000f) })
                foreach (float side in new[] { -1f, 1f })
                {
                    int a = StationAt(from), z = StationAt(to);
                    for (int i = a; i < z; i++)
                    {
                        if (WalledNear(i, side, 1) || BridgeAt(i * Spacing) > 0f || BridgeAt((i + 1) * Spacing) > 0f) continue;
                        Vector3 r0 = RightAt(pts, i) * side, r1 = RightAt(pts, i + 1) * side;
                        bool blocked = false;
                        Vector3[] q = new Vector3[4];
                        float[] e = { halfW + 0.95f, halfW + 2.75f };
                        for (int k = 0; k < 2; k++)
                        {
                            Vector3 p0 = pts[i] + r0 * e[k], p1 = pts[i + 1] + r1 * e[k];
                            if (StageKeepOut(p0.x, p0.z) && (worldKeep[(int)((p0.z - worldKeepZ0) / WorldKeepCell) * worldKeepCols + (int)((p0.x - worldKeepX0) / WorldKeepCell)] & 6) != 0) blocked = true;
                            q[k * 2] = new Vector3(p0.x, DrapeY(p0.x, p0.z) + 0.02f, p0.z); q[k * 2 + 1] = new Vector3(p1.x, DrapeY(p1.x, p1.z) + 0.02f, p1.z);
                        }
                        if (blocked) continue;
                        if (Mathf.Abs(q[0].y - q[2].y) > 0.5f || Mathf.Abs(q[1].y - q[3].y) > 0.5f) continue;   // the shoulder falls away: no walk
                        if (side < 0) buf.Quad("concrete", q[0], q[1], q[3], q[2], new Vector2(q[0].x / 2f, q[0].z / 2f), new Vector2(q[1].x / 2f, q[1].z / 2f), new Vector2(q[3].x / 2f, q[3].z / 2f), new Vector2(q[2].x / 2f, q[2].z / 2f));
                        else buf.Quad("concrete", q[0], q[2], q[3], q[1], new Vector2(q[0].x / 2f, q[0].z / 2f), new Vector2(q[2].x / 2f, q[2].z / 2f), new Vector2(q[3].x / 2f, q[3].z / 2f), new Vector2(q[1].x / 2f, q[1].z / 2f));
                        walks++;
                    }
                }
            // crosswalks: white bars across the lanes, a hair over the tarmac
            if (world.crossings != null)
                foreach (float m in world.crossings)
                {
                    int i = StationAt(m); Vector3 rt = RightAt(pts, i), fw = (pts[Mathf.Min(pts.Count - 1, i + 1)] - pts[Mathf.Max(0, i - 1)]); fw.y = 0; fw.Normalize();
                    for (float e = -halfW + 0.6f; e <= halfW - 0.6f; e += 1.2f)
                    {
                        Vector3 c = pts[i] + rt * e;
                        Vector3 a = c - rt * 0.25f - fw * 1.5f, b = c + rt * 0.25f - fw * 1.5f, cc = c + rt * 0.25f + fw * 1.5f, d = c - rt * 0.25f + fw * 1.5f;
                        foreach (var p in new[] { a, b, cc, d }) { }
                        a.y = DrapeY(a.x, a.z) + 0.008f; b.y = DrapeY(b.x, b.z) + 0.008f; cc.y = DrapeY(cc.x, cc.z) + 0.008f; d.y = DrapeY(d.x, d.z) + 0.008f;
                        buf.Quad("paint", a, b, cc, d, Vector2.zero, new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
                    }
                }
            // footways on decks: the lakeside boardwalk and the Flowering Bridge (the 1925 three-arch bridge, gardens on it)
            if (world.paths != null)
                foreach (var p in world.paths)
                {
                    if (p.deck == 0 || p.xz == null || p.xz.Length < 4) continue;
                    var line = WorldPts(p.xz);
                    float len = 0f; for (int i = 1; i < line.Length; i++) len += Vector3.Distance(line[i], line[i - 1]);
                    bool flowering = p.kind != "boardwalk" && p.st > 6150f && p.st < 6450f && len > 25f;
                    float yA = p.y[0], yB = p.y[p.y.Length - 1];
                    float deckLevel = world.lakeLevelY + 1.0f;
                    float w = flowering ? 7.0f : 2.4f;
                    for (int i = 0; i + 1 < line.Length; i++)
                    {
                        float t0 = i / (float)(line.Length - 1), t1 = (i + 1) / (float)(line.Length - 1);
                        float y0 = flowering ? Mathf.Lerp(yA, yB, t0) : Mathf.Max(deckLevel, p.y[i]), y1 = flowering ? Mathf.Lerp(yA, yB, t1) : Mathf.Max(deckLevel, p.y[i + 1]);
                        Vector3 a = line[i], b = line[i + 1];
                        Vector3 t = b - a; t.y = 0; float L = t.magnitude; if (L < 0.05f) continue; t /= L; Vector3 n = new Vector3(-t.z, 0, t.x);
                        Vector3 al = a + n * w * 0.5f, ar = a - n * w * 0.5f, bl = b + n * w * 0.5f, br = b - n * w * 0.5f;
                        al.y = ar.y = y0; bl.y = br.y = y1;
                        buf.Quad(flowering ? "concrete" : "planks", ar, br, bl, al, Vector2.zero, new Vector2(L / 2f, 0), new Vector2(L / 2f, w / 2f), new Vector2(0, w / 2f));
                        // rails / balustrade both sides
                        foreach (float sg in new[] { -1f, 1f })
                        {
                            Vector3 m = (a + b) * 0.5f + n * sg * (w * 0.5f - 0.1f); m.y = (y0 + y1) * 0.5f;
                            buf.Box(flowering ? "trim" : "darkwood", m + Vector3.up * (flowering ? 0.45f : 0.95f), t, Vector3.up, n, L * 0.5f, flowering ? 0.45f : 0.05f, flowering ? 0.18f : 0.04f, 0.5f);
                        }
                        if (flowering)
                        {
                            // garden beds down both sides of the walk
                            foreach (float sg in new[] { -1f, 1f })
                            {
                                Vector3 m0 = a + n * sg * 2.2f, m1 = b + n * sg * 2.2f, m2 = b + n * sg * 3.2f, m3 = a + n * sg * 3.2f;
                                m0.y = m3.y = y0 + 0.25f; m1.y = m2.y = y1 + 0.25f;
                                if (sg > 0) buf.Quad("mulch", m0, m1, m2, m3, Vector2.zero, new Vector2(L / 2f, 0), new Vector2(L / 2f, 0.5f), new Vector2(0, 0.5f));
                                else buf.Quad("mulch", m3, m2, m1, m0, Vector2.zero, new Vector2(L / 2f, 0), new Vector2(L / 2f, 0.5f), new Vector2(0, 0.5f));
                            }
                        }
                        else
                        {
                            // posts down to the ground
                            float g = WorldGroundY(a.x, a.z);
                            foreach (float sg in new[] { -1f, 1f })
                            {
                                Vector3 q = a + n * sg * (w * 0.5f - 0.1f);
                                if (y0 - g > 0.2f) buf.Box("darkwood", new Vector3(q.x, (g + y0) * 0.5f, q.z), t, Vector3.up, n, 0.08f, (y0 - g) * 0.5f + 0.3f, 0.08f);
                            }
                        }
                    }
                    if (flowering)
                    {
                        // three arches under the deck: spandrel faces with the arch cut as a fan, both sides
                        Vector3 a = line[0], b = line[line.Length - 1]; Vector3 t = b - a; t.y = 0; float L = t.magnitude; t /= L; Vector3 n = new Vector3(-t.z, 0, t.x);
                        float gBed = Mathf.Min(WorldGroundY((a.x + b.x) * 0.5f, (a.z + b.z) * 0.5f), world.lakeLevelY - 0.5f);
                        for (int sp = 0; sp < 3; sp++)
                        {
                            float u0 = L * sp / 3f, u1 = L * (sp + 1) / 3f, span = u1 - u0;
                            const int segs = 10;
                            foreach (float sg in new[] { -1f, 1f })
                            {
                                Vector3 face = n * sg * (3.5f);
                                for (int k = 0; k < segs; k++)
                                {
                                    float f0 = k / (float)segs, f1 = (k + 1) / (float)segs;
                                    float x0 = u0 + span * f0, x1 = u0 + span * f1;
                                    float springY = Mathf.Lerp(yA, yB, (x0 + x1) * 0.5f / L) - 0.6f;
                                    float rise = Mathf.Min(span * 0.32f, springY - gBed - 0.5f);
                                    float a0 = springY - rise + rise * (1f - Mathf.Pow(2f * f0 - 1f, 2f)) * 0f, ya0 = springY - rise * (1f - Mathf.Pow(2f * f0 - 1f, 2f)) * -1f;
                                    float arch0 = (springY - rise) + rise * (1f - (2f * f0 - 1f) * (2f * f0 - 1f));
                                    float arch1 = (springY - rise) + rise * (1f - (2f * f1 - 1f) * (2f * f1 - 1f));
                                    float deck0 = Mathf.Lerp(yA, yB, x0 / L), deck1 = Mathf.Lerp(yA, yB, x1 / L);
                                    Vector3 p0 = a + t * x0 + face, p1 = a + t * x1 + face;
                                    Vector3 lo0 = new Vector3(p0.x, arch0, p0.z), lo1 = new Vector3(p1.x, arch1, p1.z), hi0 = new Vector3(p0.x, deck0, p0.z), hi1 = new Vector3(p1.x, deck1, p1.z);
                                    if (sg > 0) buf.Quad("trim", lo0, hi0, hi1, lo1, Vector2.zero, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
                                    else buf.Quad("trim", lo1, hi1, hi0, lo0, Vector2.zero, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
                                    // the soffit under the arch
                                    if (sg > 0)
                                    {
                                        Vector3 s0 = new Vector3(p0.x, arch0, p0.z), s1 = new Vector3(p1.x, arch1, p1.z), s2 = s1 - n * 7f, s3 = s0 - n * 7f;
                                        buf.Quad("trim", s0, s1, s2, s3, Vector2.zero, new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
                                    }
                                }
                            }
                            // piers between the arches, down to the bed
                            if (sp > 0)
                            {
                                Vector3 c = a + t * u0; float top = Mathf.Lerp(yA, yB, u0 / L) - 0.6f;
                                buf.Box("trim", new Vector3(c.x, (gBed - 1f + top) * 0.5f, c.z), t, Vector3.up, n, 0.8f, (top - gBed + 1f) * 0.5f, 3.6f, 0.5f);
                            }
                        }
                    }
                    decks++;
                }
            TownEmit(buf, "Walks", parent);
            Log($"Stage world walks: {walks} sidewalk panels (flush, on the shoulder), {(world.crossings?.Length ?? 0)} crosswalks, {decks} footways on decks (the lakeside boardwalk, the Flowering Bridge).");
        }

        // ------------------------------------------------ poles, lamps, signs
        static void BuildWorldFurniture(List<Vector3> pts, Transform parent)
        {
            var buf = new TownBuf();
            float halfW = RoadWidth * 0.5f;
            int poles = 0, lamps = 0, signs = 0;
            float startM = track.stageStartLineM, finishM = track.FinishIndex * Spacing;
            bool Clear(Vector3 p, float r) => !StageKeepOut(p.x, p.z) && !StageCorridor(p.x, p.z, halfW + 1.2f + r, out _);
            void Solid(string name, Vector3 c, float radius, float height)
            {
                var go = new GameObject(name); go.transform.SetParent(parent, false); go.layer = SolidLayer;
                go.transform.position = c + Vector3.up * height * 0.5f;
                var cap = go.AddComponent<CapsuleCollider>(); cap.radius = radius; cap.height = height; cap.direction = 1;
            }
            // ---- wooden utility poles with their wires: every 45 m, on the side away from the water
            var poleAt = new List<Vector3>(); var poleSide = new List<float>();
            float sideNow = 1f; Vector3 prev = Vector3.zero; bool havePrev = false;
            for (float m = 20f; m < pts.Count * Spacing - 20f; m += 45f)
            {
                int i = StationAt(m);
                if (BridgeAt(m) > 0f) { havePrev = false; continue; }
                // the side whose ground stands higher (away from the lake and the river)
                float gR = WorldGroundY(pts[i].x + RightAt(pts, i).x * 12f, pts[i].z + RightAt(pts, i).z * 12f);
                float gL = WorldGroundY(pts[i].x - RightAt(pts, i).x * 12f, pts[i].z - RightAt(pts, i).z * 12f);
                float want = gR >= gL ? 1f : -1f;
                if (want != sideNow && Mathf.Abs(gR - gL) > 1.5f) { sideNow = want; havePrev = false; }
                Vector3 p = pts[i] + RightAt(pts, i) * sideNow * (halfW + 6.0f) + (pts[Mathf.Min(pts.Count - 1, i + 1)] - pts[i]).normalized * 2f;
                p.y = DrapeY(p.x, p.z);
                if (!Clear(p, 0.3f) || Mathf.Abs(p.y - pts[i].y) > 3f || WalledNear(i, sideNow, 2)) { havePrev = false; continue; }
                buf.Box("darkwood", p + Vector3.up * 5.2f, Vector3.right, Vector3.up, Vector3.forward, 0.13f, 6.0f, 0.13f);
                Vector3 rt = RightAt(pts, i);
                buf.Box("darkwood", p + Vector3.up * 10.4f, rt, Vector3.up, Vector3.Cross(rt, Vector3.up), 1.25f, 0.06f, 0.06f);
                Solid("Pole_" + poles, p - Vector3.up * 0.5f, 0.15f, 11.5f);
                if (havePrev)
                {
                    foreach (var (off, hgt, sag) in new[] { (-1.0f, 10.55f, 0.7f), (0f, 10.55f, 0.7f), (1.0f, 10.55f, 0.7f), (0f, 7.4f, 0.5f), (0f, 6.8f, 0.45f) })
                        Wire(buf, prev + rt * off + Vector3.up * hgt, p + rt * off + Vector3.up * hgt, sag);
                }
                prev = p; havePrev = true; poleAt.Add(p); poleSide.Add(sideNow); poles++;
            }
            // ---- lantern street lamps: Chimney Rock's Main Street and downtown Lake Lure, both sides every 32 m
            foreach (var (from, to) in new[] { (5150f, 5950f), (6950f, 7980f) })
                foreach (float side in new[] { -1f, 1f })
                    for (float m = from + (side > 0 ? 0f : 16f); m < to; m += 32f)
                    {
                        int i = StationAt(m);
                        Vector3 p = pts[i] + RightAt(pts, i) * side * (halfW + 2.2f) + (pts[Mathf.Min(pts.Count - 1, i + 1)] - pts[i]).normalized * 2f;
                        p.y = DrapeY(p.x, p.z);
                        if (StageCorridor(p.x, p.z, halfW + 1.4f, out _) || Mathf.Abs(p.y - pts[i].y) > 1.5f || WalledNear(i, side, 1)) continue;
                        if (StageKeepOut(p.x, p.z) && (worldKeep[(int)((p.z - worldKeepZ0) / WorldKeepCell) * worldKeepCols + (int)((p.x - worldKeepX0) / WorldKeepCell)] & 2) != 0) continue;
                        buf.Box("black", p + Vector3.up * 0.25f, Vector3.right, Vector3.up, Vector3.forward, 0.16f, 0.25f, 0.16f);
                        buf.Box("black", p + Vector3.up * 2.1f, Vector3.right, Vector3.up, Vector3.forward, 0.06f, 1.9f, 0.06f);
                        buf.Box("lantern", p + Vector3.up * 4.25f, Vector3.right, Vector3.up, Vector3.forward, 0.17f, 0.26f, 0.17f);
                        buf.Box("black", p + Vector3.up * 4.58f, Vector3.right, Vector3.up, Vector3.forward, 0.24f, 0.06f, 0.24f);
                        buf.Box("black", p + Vector3.up * 3.96f, Vector3.right, Vector3.up, Vector3.forward, 0.2f, 0.04f, 0.2f);
                        lamps++;
                    }
            // ---- signs: speed limits at the real zone changes, the village limits, the park, the route as words
            void Post(Vector3 at, float topY, string face, Vector3 facing, float w, float h)
            {
                buf.Box("galv", new Vector3(at.x, (at.y - 0.4f + topY) * 0.5f, at.z), Vector3.right, Vector3.up, Vector3.forward, 0.035f, (topY - at.y + 0.4f) * 0.5f, 0.035f);
                SignFace(buf, face, new Vector3(at.x, topY - h, at.z) + new Vector3(facing.x, 0, facing.z).normalized * 0.04f, facing, w, h);
                signs++;
            }
            bool PlaceSign(float m, float side, string face, float w, float h, bool forward)
            {
                for (float tryM = m; tryM < m + 40f; tryM += 4f)
                {
                    int i = StationAt(tryM);
                    Vector3 fw = pts[Mathf.Min(pts.Count - 1, i + 1)] - pts[Mathf.Max(0, i - 1)]; fw.y = 0; fw.Normalize();
                    Vector3 p = pts[i] + RightAt(pts, i) * side * (halfW + 2.6f) + fw * 2f;
                    p.y = DrapeY(p.x, p.z);
                    if (StageKeepOut(p.x, p.z) || StageCorridor(p.x, p.z, halfW + 1.6f, out _) || Mathf.Abs(p.y - pts[i].y) > 2f || WalledNear(i, side, 1) || BridgeAt(tryM) > 0f) continue;
                    Post(p, p.y + 2.1f + h, face, forward ? -fw : fw, w, h);
                    return true;
                }
                return false;
            }
            int placedSigns = 0;
            if (world.speeds != null)
                for (int k = 0; k < world.speeds.Length; k++)
                {
                    var z = world.speeds[k];
                    if (z.st < 0f) z.st = startM + 60f;
                    // forward traffic sees its new limit on the right; the twin's traffic sees the limit it enters, on its right
                    if (PlaceSign(z.st + 10f, 1f, "speed" + z.mph, 0.6f, 0.75f, true)) placedSigns++;
                    if (k > 0 && PlaceSign(z.st - 30f, -1f, "speed" + world.speeds[k - 1].mph, 0.6f, 0.75f, false)) placedSigns++;
                    if (z.mph == 20 && PlaceSign(z.st - 14f, 1f, "village", 1.8f, 0.6f, true)) placedSigns++;
                }
            if (PlaceSign(startM + 110f, 1f, "route_US64_WEST", 0.75f, 0.52f, true)) placedSigns++;
            if (PlaceSign(startM + 122f, 1f, "route_US74A_WEST", 0.75f, 0.52f, true)) placedSigns++;
            if (PlaceSign(startM + 134f, 1f, "route_NC9_NORTH", 0.75f, 0.52f, true)) placedSigns++;
            if (PlaceSign(startM + 150f, 1f, "byway", 0.9f, 0.52f, true)) placedSigns++;
            if (PlaceSign(startM + 260f, 1f, "town_LAKELURE", 1.6f, 0.48f, true)) placedSigns++;
            if (PlaceSign(finishM - 180f, -1f, "route_US64_EAST", 0.75f, 0.52f, false)) placedSigns++;
            if (PlaceSign(finishM - 192f, -1f, "route_US74A_EAST", 0.75f, 0.52f, false)) placedSigns++;
            if (PlaceSign(finishM - 204f, -1f, "route_NC9_SOUTH", 0.75f, 0.52f, false)) placedSigns++;
            if (PlaceSign(10950f, 1f, "town_BATCAVE", 1.6f, 0.48f, true)) placedSigns++;
            if (world.parkRoad != null && world.parkRoad.mouthSt > 0f)
            {
                float side = -world.parkRoad.side;
                if (PlaceSign(world.parkRoad.mouthSt - 70f, side, "park", 1.8f, 0.75f, true)) placedSigns++;
            }
            if (world.crossings != null)
                foreach (float m in world.crossings)
                {
                    if (PlaceSign(m - 40f, 1f, "ped", 0.75f, 0.75f, true)) placedSigns++;
                    if (PlaceSign(m + 40f, -1f, "ped", 0.75f, 0.75f, false)) placedSigns++;
                }
            TownEmit(buf, "Furniture", parent);
            Log($"Stage world furniture: {poles} wooden utility poles with their wires (45 m, the side away from the water, 6 m past the tarmac edge, solid), {lamps} lantern lamps and {placedSigns} signs (thin posts, drawn and not solid, as the reflector posts).");
        }

        /// <summary>One wire between two points, sagging: a thin cross of two
        /// quads per segment, so it reads from any angle.</summary>
        static void Wire(TownBuf b, Vector3 a, Vector3 c, float sag)
        {
            const int segs = 8; const float th = 0.025f;
            Vector3 prev = a;
            for (int k = 1; k <= segs; k++)
            {
                float t = k / (float)segs;
                Vector3 p = Vector3.Lerp(a, c, t) - Vector3.up * (sag * 4f * t * (1f - t));
                Vector3 d = (p - prev).normalized, s1 = Vector3.Cross(d, Vector3.up).normalized * th, s2 = Vector3.up * th;
                b.Quad("black", prev - s1, p - s1, p + s1, prev + s1, Vector2.zero, new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
                b.Quad("black", prev + s1, p + s1, p - s1, prev - s1, Vector2.zero, new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
                b.Quad("black", prev - s2, p - s2, p + s2, prev + s2, Vector2.zero, new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
                b.Quad("black", prev + s2, p + s2, p - s2, prev - s2, Vector2.zero, new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
                prev = p;
            }
        }
    }
}
