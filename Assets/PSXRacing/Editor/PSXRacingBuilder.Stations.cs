using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CHARLOTTE'S FILLING STATIONS, BAKED (city fuel r1, PHASE B). The part
    /// prefabs CityWorld stands a station from (<see cref="CityStations.Stand"/>),
    /// cut from the owner's Gas_station pack exactly as the town and the
    /// circuits stand it (<see cref="SpawnStation"/>: its one layout, scaled off
    /// its pumps to 2.2 m), into Resources/CityStations:
    ///   StationCanopy1/2/3  the canopy MODULE turned a quarter - the owner's
    ///                       DOT rule wants the pumps ALONG the frontage, the
    ///                       pack runs its islands away from the road - laid
    ///                       1-3 times side by side: roof, pillars, islands,
    ///                       lamp panels on the station atlas; the four pumps a
    ///                       module detailed to 40 m, the pack's own hose-less
    ///                       68-triangle pump (from Gas_station_Props.fbx) to
    ///                       150 m, the shell alone to 400 m; a box collider
    ///                       per pillar, island and pump body; a fuelling
    ///                       volume per pump (GasPump, 7 x 3 x 5.5 m along its
    ///                       island, as the circuits'); four CanopyLight markers
    ///                       a module (the stand-up's white-green NightGlow heads)
    ///   StationShop         the shop's outside only (the store opens as a
    ///                       menu at its door; the room is the town's), the badge
    ///                       on its own renderer (PUMPWREN at the pack's
    ///                       resolution, culled at 150 m), its glass opaque and
    ///                       lit yellow after dark as a shopfront (_NightWin, a
    ///                       mask of all shopfront), one box collider, the
    ///                       StoreDoor marker 1.4 m out from its front door
    ///   StationRestrooms    the restroom block's outside, one box
    ///   StationDumpster     the dumpster and its bins, one box
    ///   StationPylon        the price pylon, its lightbox glowing in its own
    ///                       colours after dark through the emissor
    ///                       (_NightFace + _NightMask: fluorescent white-green,
    ///                       the flip numerals dark), one box
    /// Each prefab's frame is the pack's own: x along the road, z away from it
    /// (the canopy's after its quarter turn), floor at y = 0, the part's middle
    /// at the origin; the shop's door looks down -z, the restrooms' down -x.
    /// Every opaque pack material but the pumps', the badge's, the glass and
    /// the pylon's shares ONE 256 px atlas (CityPropBaker's PSX_ATLAS_RECT
    /// cells), so a station is 8 draws near and 4 far.
    ///
    /// THE PART CHECK: every part's measured size against CityStations' table
    /// (stations.mjs's numbers): a part off by more than CityStations.PartTolM
    /// FAILS the bake - the stations would stand on lots sized for another
    /// model. Report: PSXRacing_city_stations.txt.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        const string StationPrefabDir = Root + "/Resources/CityStations";
        const string StationArtDir = Root + "/Art/GasStation/City";
        /// <summary>The LOD distances are set for this vertical field of view
        /// at LOD bias 1 (QualitySettings: Mobile 1, PC 2 - a PC keeps each
        /// level twice as far).</summary>
        const float StationLodFovDeg = 60f;
        const float PumpNearM = 40f, PumpFarM = 150f, StationFarM = 400f;

        sealed class StationPart
        {
            public string name;
            public Vector3 origin;          // world, on the floor
            public Vector3 xAxis, zAxis;    // world: the prefab's +x and +z
            public readonly List<MeshRenderer> pieces = new List<MeshRenderer>();
            public Matrix4x4 ToLocal => Matrix4x4.TRS(origin, Quaternion.LookRotation(zAxis, Vector3.up), Vector3.one).inverse;
        }

        [MenuItem("PSX Racing/Bake City Filling Stations")]
        public static void BakeCityStationsMenu()
        {
            foreach (var l in BakeCityStations()) Debug.Log("[Stations] " + l);
        }

        /// <summary>Bake the part prefabs; the report lines (a line starting
        /// FAILED means the stations must not ship).</summary>
        public static List<string> BakeCityStations()
        {
            if (log == null) log = new System.Text.StringBuilder();
            var report = new List<string>();
            GameObject src = null, props = null;
            var made = new List<GameObject>();
            try
            {
                if (psxLit == null) psxLit = Shader.Find("PSX/Lit");
                if (psxLit == null) throw new Exception("PSX/Lit shader not found");
                CityPropBaker.EnsureFolder(Root + "/Resources", "CityStations");
                CityPropBaker.EnsureFolder(Root + "/Art/GasStation", "City");

                // ---- the station, as the town and the circuits stand it
                src = SpawnStation("StationBake", out var pumps);
                src.transform.position = new Vector3(0f, 6000f, 0f);   // above anything an open scene holds
                // the GLASS before the materials are converted (the slots whose
                // pack material is a window pane: opaque and lit at night here)
                var glassSlots = new Dictionary<MeshRenderer, bool[]>();
                foreach (var r in src.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var ms = r.sharedMaterials;
                    bool[] g = null;
                    for (int i = 0; i < ms.Length; i++)
                        if (ms[i] != null && IsGlassName(ms[i].name)) (g ??= new bool[ms.Length])[i] = true;
                    if (g != null) glassSlots[r] = g;
                }
                ConvertToPSXMaterials(src);
                float scale = src.transform.localScale.x;
                if (pumps.Count != 4) report.Add($"NOTE the station has {pumps.Count} pumps (the pack's layout has 4)");
                report.Add($"the station: x{scale:0.0000} (pumps to {PumpHeightM} m), {pumps.Count} pumps");

                var leaves = new List<MeshRenderer>();
                foreach (var r in src.GetComponentsInChildren<MeshRenderer>(false))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) leaves.Add(r);
                }
                MeshRenderer One(string n) => leaves.Find(r => r.name == n) ?? throw new Exception("the pack's '" + n + "' is not in the station");
                var ceiling = One("The_ceiling");
                var shell = One("6twelve");
                var bath = One("Bathrooms");
                var pylon = One("6twelve_Sign");
                var dump = One("Dumpster");
                var pumpRends = new HashSet<MeshRenderer>();
                foreach (var p in pumps) foreach (var r in p.GetComponentsInChildren<MeshRenderer>(false)) pumpRends.Add(r);

                // ---- the frame: the pack is axis-aligned; v from the canopy
                // to the shop (away from the road), u its right
                float floorY = float.MaxValue;
                foreach (var p in pumps) floorY = Mathf.Min(floorY, CombinedBounds(p.gameObject).min.y);
                var toShop = shell.bounds.center - ceiling.bounds.center; toShop.y = 0f;
                Vector3 vAxis = Mathf.Abs(toShop.x) > Mathf.Abs(toShop.z) ? new Vector3(Mathf.Sign(toShop.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(toShop.z));
                Vector3 uAxis = new Vector3(vAxis.z, 0f, -vAxis.x);
                float Along(Bounds b, Vector3 axis) => Mathf.Abs(Vector3.Dot(b.size, axis));
                float Coord(Vector3 p, Vector3 axis) => Vector3.Dot(p, axis);
                Vector3 Floor(Vector3 c) => new Vector3(c.x, floorY, c.z);

                StationPart Part(string name, Vector3 centre, Vector3 x, Vector3 z) => new StationPart { name = name, origin = Floor(centre), xAxis = x, zAxis = z };
                // the canopy turned a quarter: its islands (along v) along the road (+x)
                var canopy = Part("canopy", ceiling.bounds.center, vAxis, -uAxis);
                var shop = Part("shop", shell.bounds.center, uAxis, vAxis);
                var wc = Part("restrooms", bath.bounds.center, uAxis, vAxis);
                var dumpster = Part("dumpster", dump.bounds.center, uAxis, vAxis);
                var sign = Part("pylon", pylon.bounds.center, uAxis, vAxis);

                // ---- every leaf to its part, or dropped (and said why)
                var cb = ceiling.bounds; var sb = shell.bounds; var bb = bath.bounds; var db = dump.bounds;
                float shopFront = Mathf.Min(Coord(sb.min, vAxis), Coord(sb.max, vAxis));
                float shopU0 = Mathf.Min(Coord(sb.min, uAxis), Coord(sb.max, uAxis)), shopU1 = Mathf.Max(Coord(sb.min, uAxis), Coord(sb.max, uAxis));
                bool In(Bounds o, Vector3 p, float pad) => p.x >= o.min.x - pad && p.x <= o.max.x + pad && p.z >= o.min.z - pad && p.z <= o.max.z + pad;
                bool Starts(string n, params string[] ps) { foreach (var s in ps) if (n.StartsWith(s, StringComparison.OrdinalIgnoreCase)) return true; return false; }
                MeshRenderer frontDoor = null;
                var dropped = new SortedDictionary<string, List<string>>();
                void Drop(string why, string n) { if (!dropped.TryGetValue(why, out var l)) dropped[why] = l = new List<string>(); l.Add(n); }
                foreach (var r in leaves)
                {
                    if (pumpRends.Contains(r)) continue;   // the pumps are their own meshes
                    string n = r.name;
                    var c = r.bounds.center;
                    // (the canopy's lamp panels are under its roof, half its height and more up;
                    // the shop's own are inside the shop, never under the canopy)
                    if (r == ceiling || (Starts(n, "Pillar", "Sidewalk") && In(cb, c, 0.5f)) ||
                        (Starts(n, "Lamp") && In(cb, c, 0.5f) && c.y > floorY + 0.5f * (cb.min.y - floorY)))
                    { canopy.pieces.Add(r); continue; }
                    if (r == pylon) { sign.pieces.Add(r); continue; }
                    if ((r == dump || Starts(n, "Dumpster", "Trash")) && (r.bounds.center - db.center).magnitude < 4f) { dumpster.pieces.Add(r); continue; }
                    if (r == bath || (Starts(n, "Door_01", "Frame_04", "Toilets_Sing", "Asphalt_02") && In(bb, c, 2.5f))) { wc.pieces.Add(r); continue; }
                    if (In(bb, c, 0.3f)) { Drop("restroom interior", n); continue; }
                    bool outside = r == shell || (Starts(n, "6twelve", "Glass", "Window_frame", "spotlight", "ICE", "Asphalt_01") && In(sb, c, 1.5f));
                    if (!outside && Starts(n, "Door", "Frame") && In(sb, c, 0.5f))
                    {
                        float cu = Coord(c, uAxis), cv = Coord(c, vAxis);
                        if (Mathf.Abs(cv - shopFront) < 0.6f + 0.6f * scale && cu > shopU0 && cu < shopU1)
                        {
                            outside = true;
                            if (Starts(n, "Door") && frontDoor == null) frontDoor = r;
                        }
                    }
                    if (outside) { shop.pieces.Add(r); continue; }
                    if (In(sb, c, 0.3f)) { Drop("shop interior", n); continue; }
                    Drop(r.bounds.size.x > 25f || r.bounds.size.z > 25f ? "the pack's lot and landscape" : "beyond the station's parts", n);
                }
                foreach (var kv in dropped)
                    report.Add($"dropped ({kv.Key}): {kv.Value.Count}: " + string.Join(", ", kv.Value.GetRange(0, Mathf.Min(14, kv.Value.Count))) + (kv.Value.Count > 14 ? ", ..." : ""));

                // ---- the materials the atlas does not take
                var pumpMat = pumps.Count > 0 ? pumps[0].GetComponentInChildren<MeshRenderer>().sharedMaterial : null;
                var glassMat = StationGlassMaterial();
                var pylonMat = StationPylonMaterial(pylon.sharedMaterial);
                bool Badge(Material m) => m != null && m.mainTexture != null && m.mainTexture.name == "6twelve";
                bool Apart(Material m) => m == null || m == pumpMat || Badge(m) || m == glassMat || m == pylonMat;

                // ---- the pieces of each part in its own frame
                List<CityPropBaker.Piece> Pieces(StationPart part, Func<MeshRenderer, bool> take, Vector3 shift)
                {
                    var list = new List<CityPropBaker.Piece>();
                    var M = Matrix4x4.Translate(shift) * part.ToLocal;
                    foreach (var r in part.pieces)
                    {
                        if (!take(r)) continue;
                        var mats = (Material[])r.sharedMaterials.Clone();
                        if (glassSlots.TryGetValue(r, out var g))
                            for (int i = 0; i < mats.Length && i < g.Length; i++) if (g[i]) mats[i] = glassMat;
                        if (r == pylon) for (int i = 0; i < mats.Length; i++) mats[i] = pylonMat;
                        list.Add(new CityPropBaker.Piece { r = r, mesh = r.GetComponent<MeshFilter>().sharedMesh, toRoot = M * r.transform.localToWorldMatrix, mats = mats });
                    }
                    return list;
                }

                // ---- THE ATLAS: every part's atlasable materials, weighted by the surface they cover
                var texes = new List<CityPropBaker.TexInfo>();
                foreach (var part in new[] { canopy, shop, wc, dumpster })
                    foreach (var pc in Pieces(part, r => true, Vector3.zero))
                        for (int s = 0; s < pc.mats.Length && s < pc.mesh.subMeshCount; s++)
                        {
                            var m = pc.mats[s];
                            if (Apart(m) || !CityPropBaker.Atlasable(m)) continue;
                            CityPropBaker.FindOrAdd(texes, m).area += CityPropBaker.SurfaceArea(pc.mesh, s, pc.toRoot);
                        }
                if (texes.Count == 0) throw new Exception("nothing to atlas");
                CityPropBaker.PlanCells(texes);
                var atlasMat = CityPropBaker.WriteAtlas("station", texes);
                var sizes = new List<string>();
                foreach (var t in texes) sizes.Add($"{(t.tex != null ? t.tex.name : "white")} {t.size}");
                report.Add($"atlas 'station' {CityPropBaker.AtlasPx}px, {texes.Count} textures: " + string.Join(", ", sizes));

                // ---- THE PART CHECK
                bool partsOk = true;
                void PartCheck(string what, float got, float want)
                {
                    bool ok = Mathf.Abs(got - want) <= CityStations.PartTolM;
                    partsOk &= ok;
                    report.Add($"{(ok ? "part check ok" : "FAILED part check")}: {what} {got:0.00} m (CityStations {want:0.00})");
                }
                float W = Along(cb, vAxis), D = Along(cb, uAxis);
                PartCheck("canopy module along the road", W, CityStations.CanW);
                PartCheck("canopy module deep", D, CityStations.CanD);
                PartCheck("shop along the road", Along(sb, uAxis), CityStations.ShopW);
                PartCheck("shop deep", Along(sb, vAxis), CityStations.ShopD);
                PartCheck("restrooms along the road", Along(bb, uAxis), CityStations.WcW);
                PartCheck("restrooms deep", Along(bb, vAxis), CityStations.WcD);
                PartCheck("pylon deep", Along(pylon.bounds, vAxis), CityStations.PylonDeepM);

                // ---- the pack's hose-less pump, for the far LOD
                MeshRenderer farPump = null;
                var propsAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Art/GasStation/Gas_station_Props.fbx");
                if (propsAsset != null)
                {
                    props = (GameObject)UnityEngine.Object.Instantiate(propsAsset);
                    props.transform.localScale = Vector3.one * scale;
                    props.transform.position = src.transform.position;
                    ConvertToPSXMaterials(props);
                    foreach (var r in props.GetComponentsInChildren<MeshRenderer>(true))
                        if (r.name == "Fuel_pump_") { farPump = r; break; }
                }
                if (farPump == null) report.Add("NOTE no Fuel_pump_ in Gas_station_Props.fbx: the far pumps are the detailed ones");

                // ---- THE CANOPIES: 1-3 modules, side by side along the road
                for (int n = 1; n <= 3; n++)
                {
                    var root = new GameObject(CityStations.CanopyPrefab + n);
                    made.Add(root);
                    var shellPieces = new List<CityPropBaker.Piece>();
                    var farShellPieces = new List<CityPropBaker.Piece>();   // the far box: roof and legs only
                    var nearPumps = new List<CityPropBaker.Piece>();
                    var farPumps = new List<CityPropBaker.Piece>();
                    int triggers = 0, solids = 0;
                    for (int k = 0; k < n; k++)
                    {
                        var shift = new Vector3((k - 0.5f * (n - 1)) * W, 0f, 0f);
                        shellPieces.AddRange(Pieces(canopy, r => true, shift));
                        farShellPieces.AddRange(Pieces(canopy, r => r == ceiling || Starts(r.name, "Pillar"), shift));
                        var M = Matrix4x4.Translate(shift) * canopy.ToLocal;
                        foreach (var p in pumps)
                        {
                            foreach (var r in p.GetComponentsInChildren<MeshRenderer>(false))
                                nearPumps.Add(new CityPropBaker.Piece { r = r, mesh = r.GetComponent<MeshFilter>().sharedMesh, toRoot = M * r.transform.localToWorldMatrix, mats = r.sharedMaterials });
                            var pb = CombinedBounds(p.gameObject);
                            Matrix4x4 farM = Matrix4x4.identity;
                            Bounds bodyB = pb;
                            if (farPump != null)
                            {
                                var lb = farPump.bounds;
                                bool pLong = pb.size.x > pb.size.z, lLong = lb.size.x > lb.size.z;
                                farM = Matrix4x4.Translate(new Vector3(pb.center.x, pb.min.y, pb.center.z)) *
                                       Matrix4x4.Rotate(pLong == lLong ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f)) *
                                       Matrix4x4.Translate(-new Vector3(lb.center.x, lb.min.y, lb.center.z)) * farPump.transform.localToWorldMatrix;
                                farPumps.Add(new CityPropBaker.Piece { r = farPump, mesh = farPump.GetComponent<MeshFilter>().sharedMesh, toRoot = M * farM, mats = new[] { pumpMat ?? farPump.sharedMaterial } });
                                bodyB = TransformedBounds(farM * farPump.transform.worldToLocalMatrix, lb);   // the body, no hoses
                            }
                            // its solid body and its fuelling volume, in the canopy's frame
                            var localBody = TransformedBounds(M, bodyB);
                            AddBox(root, "PumpSolid", localBody.center, localBody.size, SolidLayer);
                            solids++;
                            var vol = new GameObject("Pump");
                            vol.transform.SetParent(root.transform, false);
                            vol.transform.localPosition = new Vector3(localBody.center.x, 1.1f, localBody.center.z);
                            var trig = vol.AddComponent<BoxCollider>();
                            trig.isTrigger = true;
                            trig.size = new Vector3(7f, 3f, 5.5f);   // along the island, up, across it (the circuits')
                            vol.AddComponent<GasPump>();
                            triggers++;
                        }
                        // pillars and islands stop a car; the roof is overhead
                        foreach (var r in canopy.pieces)
                        {
                            if (Starts(r.name, "Pillar"))
                                foreach (var lb in LegBoxes(r, M)) { AddBox(root, "PillarSolid", lb.center, lb.size, SolidLayer); solids++; }
                            else if (Starts(r.name, "Sidewalk"))
                            {
                                var lb = TransformedBounds(M, r.bounds);
                                AddBox(root, "IslandSolid", lb.center, lb.size, SolidLayer);
                                solids++;
                            }
                        }
                        // the lights: four heads a module, under the roof
                        float lampY = 0f; int lamps = 0;
                        foreach (var r in canopy.pieces) if (Starts(r.name, "Lamp")) { lampY += M.MultiplyPoint3x4(r.bounds.center).y; lamps++; }
                        lampY = lamps > 0 ? lampY / lamps - 0.05f : 5.0f;
                        for (int j = 0; j < 4; j++)
                        {
                            var mk = new GameObject(CityStations.LightMarker);
                            mk.transform.SetParent(root.transform, false);
                            mk.transform.localPosition = shift + new Vector3(((j & 1) == 0 ? -0.25f : 0.25f) * W, lampY, ((j & 2) == 0 ? -0.25f : 0.25f) * D);
                        }
                    }
                    var shellR = MergedRenderer(root, "Shell", shellPieces, texes, atlasMat, StationPrefabDir + "/" + root.name + "_shell.asset");
                    var nearR = MergedRenderer(root, "Pumps", nearPumps, null, null, StationPrefabDir + "/" + root.name + "_pumps.asset");
                    var farR = farPumps.Count > 0 ? MergedRenderer(root, "PumpsFar", farPumps, null, null, StationPrefabDir + "/" + root.name + "_pumpsfar.asset") : null;
                    var boxR = MergedRenderer(root, "ShellFar", farShellPieces, texes, atlasMat, StationPrefabDir + "/" + root.name + "_far.asset");
                    StationLods(root, (new Renderer[] { shellR, nearR }, PumpNearM), (farR != null ? new Renderer[] { shellR, farR } : new Renderer[] { shellR, nearR }, PumpFarM),
                                (new Renderer[] { boxR ?? shellR }, StationFarM));
                    SaveStationPrefab(root, report, $"{n} module(s): {triggers} pumps, {solids} boxes, {4 * n} lights, " +
                        $"{TrisOf(shellR)} + {TrisOf(nearR)} tris near, {TrisOf(shellR)} + {TrisOf(farR)} to {PumpFarM:0} m, {TrisOf(boxR)} to {StationFarM:0} m");
                }

                // ---- THE SHOP: its outside; the badge on its own renderer; glass lit
                {
                    var root = new GameObject(CityStations.ShopPrefab);
                    made.Add(root);
                    var shellR = MergedRenderer(root, "Shell", Pieces(shop, r => !HasBadge(r, Badge), Vector3.zero), texes, atlasMat, StationPrefabDir + "/StationShop_shell.asset");
                    var badgeR = MergedRenderer(root, "Badge", Pieces(shop, r => HasBadge(r, Badge), Vector3.zero), null, null, StationPrefabDir + "/StationShop_badge.asset");
                    // the far box: the walls, the roof and the glass (lit at night: a
                    // forecourt's shop window is the strip's light from afar)
                    var farR = MergedRenderer(root, "ShellFar", Pieces(shop, r => r == shell || r.name == "6twelve.003" || glassSlots.ContainsKey(r), Vector3.zero),
                                              texes, atlasMat, StationPrefabDir + "/StationShop_far.asset");
                    var lb = TransformedBounds(shop.ToLocal, sb);
                    // the box's front at the glass, not at the fascia over the
                    // entrance (no invisible wall under the overhang)
                    if (frontDoor != null)
                    {
                        float glassZ = shop.ToLocal.MultiplyPoint3x4(frontDoor.bounds.center).z;
                        if (glassZ > lb.min.z && glassZ < lb.center.z) lb.SetMinMax(new Vector3(lb.min.x, lb.min.y, glassZ), lb.max);
                    }
                    AddBox(root, "ShopSolid", lb.center, lb.size, SolidLayer);
                    // the door marker: 1.4 m out from the front door, at eye height
                    float doorX = frontDoor != null ? shop.ToLocal.MultiplyPoint3x4(frontDoor.bounds.center).x : 0f;
                    var door = new GameObject(PSXRacing.OnFoot.StoreDoor.ObjectName);
                    door.transform.SetParent(root.transform, false);
                    door.transform.localPosition = new Vector3(doorX, 1.2f, lb.min.z - 1.4f);
                    door.AddComponent<PSXRacing.OnFoot.StoreDoor>();
                    StationLods(root, (new Renderer[] { shellR, badgeR }, PumpFarM), (new Renderer[] { farR ?? shellR }, StationFarM));
                    SaveStationPrefab(root, report, $"front door {(frontDoor != null ? frontDoor.name : "NOT FOUND (door marker at the front's middle)")}, " +
                        $"{TrisOf(shellR)} + {TrisOf(badgeR)} tris to {PumpFarM:0} m, {TrisOf(farR)} to {StationFarM:0} m");
                }
                // ---- THE RESTROOMS, THE DUMPSTER, THE PYLON
                foreach (var (part, name, keepM, box) in new[]
                {
                    (wc, CityStations.WcPrefab, 200f, bb),
                    (dumpster, CityStations.DumpsterPrefab, 80f, db),
                    (sign, CityStations.PylonPrefab, 450f, pylon.bounds),
                })
                {
                    var root = new GameObject(name);
                    made.Add(root);
                    bool own = part == sign;   // the pylon keeps its night material
                    var r = MergedRenderer(root, "Shell", Pieces(part, x => true, Vector3.zero), own ? null : texes, own ? null : atlasMat, StationPrefabDir + "/" + name + "_shell.asset");
                    var lb = TransformedBounds(part.ToLocal, box);
                    AddBox(root, "Solid", lb.center, lb.size, SolidLayer);
                    StationLods(root, (new Renderer[] { r }, keepM));
                    SaveStationPrefab(root, report, $"{TrisOf(r)} tris, kept to {keepM:0} m");
                }
                AssetDatabase.SaveAssets();
                if (!partsOk) report.Add("FAILED: a part does not match CityStations' table - the stations would stand on lots sized for another model");
            }
            catch (Exception e)
            {
                report.Add("FAILED: " + e.Message);
                Debug.LogException(e);
            }
            finally
            {
                if (src != null) UnityEngine.Object.DestroyImmediate(src);
                if (props != null) UnityEngine.Object.DestroyImmediate(props);
                foreach (var go in made) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            File.WriteAllLines(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_city_stations.txt"), report);
            return report;
        }

        /// <summary>
        /// A pillar's LEGS as boxes in the frame <paramref name="M"/>. The pack
        /// draws an island's two canopy legs as ONE object eleven metres long
        /// (AddStationPieceColliders' note), whose box would be a wall straight
        /// through the pump line: its vertices are split at the middle of its
        /// long side and each half boxed. One box when a half spans more than
        /// 40 % of the length (a solid wall after all).
        /// </summary>
        static List<Bounds> LegBoxes(MeshRenderer r, Matrix4x4 M)
        {
            var mesh = r.GetComponent<MeshFilter>().sharedMesh;
            var W = M * r.transform.localToWorldMatrix;
            var pts = new List<Vector3>();
            using (var arr = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                var d = arr[0];
                var v = new Unity.Collections.NativeArray<Vector3>(d.vertexCount, Unity.Collections.Allocator.Temp);
                d.GetVertices(v);
                for (int i = 0; i < v.Length; i++) pts.Add(W.MultiplyPoint3x4(v[i]));
                v.Dispose();
            }
            var outList = new List<Bounds>();
            if (pts.Count == 0) { outList.Add(TransformedBounds(M, r.bounds)); return outList; }
            var all = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) all.Encapsulate(p);
            bool alongX = all.size.x >= all.size.z;
            float mid = alongX ? all.center.x : all.center.z, len = alongX ? all.size.x : all.size.z;
            Bounds a = default, b = default;
            bool hasA = false, hasB = false;
            foreach (var p in pts)
            {
                if ((alongX ? p.x : p.z) < mid) { if (!hasA) { a = new Bounds(p, Vector3.zero); hasA = true; } else a.Encapsulate(p); }
                else { if (!hasB) { b = new Bounds(p, Vector3.zero); hasB = true; } else b.Encapsulate(p); }
            }
            float La = hasA ? (alongX ? a.size.x : a.size.z) : len, Lb = hasB ? (alongX ? b.size.x : b.size.z) : len;
            if (hasA && hasB && La < 0.4f * len && Lb < 0.4f * len) { outList.Add(a); outList.Add(b); }
            else outList.Add(all);
            return outList;
        }

        static bool HasBadge(MeshRenderer r, Func<Material, bool> badge)
        {
            foreach (var m in r.sharedMaterials) if (badge(m)) return true;
            return false;
        }

        /// <summary>The world box of <paramref name="b"/>'s corners through M.</summary>
        static Bounds TransformedBounds(Matrix4x4 M, Bounds b)
        {
            var o = new Bounds(M.MultiplyPoint3x4(b.min), Vector3.zero);
            for (int i = 0; i < 8; i++)
                o.Encapsulate(M.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z)));
            return o;
        }

        static void AddBox(GameObject root, string name, Vector3 centre, Vector3 size, int layer)
        {
            var g = new GameObject(name) { layer = layer };
            g.transform.SetParent(root.transform, false);
            g.transform.localPosition = centre;
            var bc = g.AddComponent<BoxCollider>();
            bc.size = new Vector3(Mathf.Max(0.05f, size.x), Mathf.Max(0.05f, size.y), Mathf.Max(0.05f, size.z));
        }

        /// <summary>The pieces merged into one renderer under the root (the
        /// atlased materials as one submesh when an atlas is given), its mesh
        /// saved; null when there is nothing to draw.</summary>
        static MeshRenderer MergedRenderer(GameObject root, string name, List<CityPropBaker.Piece> pieces,
                                           List<CityPropBaker.TexInfo> texes, Material atlasMat, string meshPath)
        {
            if (pieces.Count == 0) return null;
            var mesh = CityPropBaker.MergePieces(pieces, texes, atlasMat, new List<Material>(), out var mats);
            if (mesh.vertexCount == 0) return null;
            mesh.name = Path.GetFileNameWithoutExtension(meshPath);
            CityPropBaker.SaveMesh(mesh, meshPath);
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) ?? mesh;
            var g = new GameObject(name);
            g.transform.SetParent(root.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = saved;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        static int TrisOf(Renderer r)
        {
            var mf = r != null ? r.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) return 0;
            int t = 0;
            for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) t += (int)mf.sharedMesh.GetIndexCount(s) / 3;
            return t;
        }

        /// <summary>A LODGroup whose levels end at these distances (metres, at
        /// <see cref="StationLodFovDeg"/> and LOD bias 1), culled past the last.</summary>
        static void StationLods(GameObject root, params (Renderer[] rs, float toM)[] levels)
        {
            var g = root.AddComponent<LODGroup>();
            g.fadeMode = LODFadeMode.None;
            var lods = new LOD[levels.Length];
            for (int i = 0; i < levels.Length; i++) lods[i] = new LOD(1f - 0.1f * (i + 1), Array.FindAll(levels[i].rs, r => r != null));
            g.SetLODs(lods);
            g.RecalculateBounds();
            float size = Mathf.Max(g.size, 0.5f);
            float t = 2f * Mathf.Tan(0.5f * StationLodFovDeg * Mathf.Deg2Rad);
            float prev = 1f;
            for (int i = 0; i < levels.Length; i++)
            {
                float h = Mathf.Clamp(size / (levels[i].toM * t), 0.0002f, prev - 0.0001f);
                lods[i] = new LOD(h, Array.FindAll(levels[i].rs, r => r != null));
                prev = h;
            }
            g.SetLODs(lods);
        }

        static void SaveStationPrefab(GameObject root, List<string> report, string what)
        {
            string path = StationPrefabDir + "/" + root.name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            report.Add($"{root.name}: {CityPropBaker.DrawsOf(root)} draws, {root.GetComponentsInChildren<Collider>(true).Length} colliders; {what}");
        }

        /// <summary>The shop's glass in the city: opaque (no room behind it),
        /// dark by day, lit after dark as a shopfront (PSX/Lit's window
        /// path, WIN_SHOP: the 90s rule's yellow, never blue) through a mask
        /// that is all shopfront.</summary>
        static Material StationGlassMaterial()
        {
            string maskPath = StationArtDir + "/StationGlass_night.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath) == null)
            {
                var t = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
                var px = new Color32[16];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 128, 255, 255);   // R window, G its id, B shopfront, A window
                t.SetPixels32(px); t.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), maskPath), t.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(maskPath, ImportAssetOptions.ForceUpdate);
            }
            if (AssetImporter.GetAtPath(maskPath) is TextureImporter imp &&
                (imp.mipmapEnabled || imp.filterMode != FilterMode.Point || imp.sRGBTexture || imp.textureCompression != TextureImporterCompression.Uncompressed))
            {
                imp.mipmapEnabled = false; imp.filterMode = FilterMode.Point; imp.sRGBTexture = false;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
            var mat = StationMat("StationGlassCity", null);
            mat.color = new Color(0.20f, 0.24f, 0.26f, 1f);
            mat.SetTexture("_NightMask", mask);
            mat.SetFloat("_NightWin", mask != null ? 1f : 0f);
            mat.SetFloat("_NightFace", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>The pylon in the city: the pack's (brand-free) face, its
        /// lightbox lit in its own colours after dark through the emissor.</summary>
        static Material StationPylonMaterial(Material converted)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/GasStation/Textures/6twelve_Sign.png");
            var emissor = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/GasStation/Textures/6twelve_Sign_Emissor.png");
            var mat = StationMat("StationPylonCity", tex);
            if (converted != null)
            {
                mat.mainTextureScale = converted.mainTextureScale;
                mat.mainTextureOffset = converted.mainTextureOffset;
                if (converted.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", converted.GetFloat("_Cutoff"));
                mat.renderQueue = converted.renderQueue;
            }
            mat.SetTexture("_NightMask", emissor);
            mat.SetFloat("_NightWin", emissor != null ? 1f : 0f);
            mat.SetFloat("_NightFace", emissor != null ? 1f : 0f);
            if (emissor == null) Log("WARN: 6twelve_Sign_Emissor.png missing - the city's price pylons stay dark at night");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material StationMat(string name, Texture tex)
        {
            string path = StationArtDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(psxLit); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = psxLit;
            mat.mainTexture = tex;
            mat.color = Color.white;
            mat.SetFloat("_Cutoff", 0f);
            if (mat.HasProperty("_Affine")) mat.SetFloat("_Affine", 0f);
            if (mat.HasProperty("_Wet")) mat.SetFloat("_Wet", 0f);
            mat.DisableKeyword("PSX_ATLAS_RECT");
            mat.renderQueue = -1;
            return mat;
        }
    }
}
