using System.IO;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The line model at named places (WP-11b diagnostics): PSX_LM_NODES =
    /// "n,n,..." prints each node's arms (trim, extents at the trim, the
    /// taper eases) and its fan perimeter; PSX_LM_EDGES = "e,e,..." prints each
    /// edge's model (offset, extents, eases, lines at a few stations) and its
    /// sections as the last tile build cut them. Writes lm_probe.txt at the
    /// project root.
    ///   -executeMethod PSXRacing.EditorTools.LineModelProbe.Run
    /// </summary>
    public static class LineModelProbe
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            var map = CityMap.Get();
            var trims = CityMeshes.NodeTrims(map);
            var la = new System.Collections.Generic.List<LineModel.LineAt>();
            foreach (var tok in (System.Environment.GetEnvironmentVariable("PSX_LM_NODES") ?? "").Split(','))
            {
                if (!int.TryParse(tok, out int n)) continue;
                var np = map.nodes[n];
                sb.AppendLine($"node {n} at ({np.x:0.0}, {np.y:0.0}) deg {map.nodeEdges[n].Count} patch {trims.patch[n]} mitre {trims.mitre[n]}");
                foreach (var ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    float trim = trims.TrimAt(e, n), at = e.a == n ? trim : e.length - trim;
                    LineModel.Extents(e, at, out float eM, out float eP);
                    var t = e.TangentAt(at);
                    float ang = Mathf.Atan2(e.a == n ? t.y : -t.y, e.a == n ? t.x : -t.x) * Mathf.Rad2Deg;
                    sb.AppendLine($"  e{ei} '{e.name}' {RoadProfiles.All[e.profile].key} {(e.a == n ? "a" : "b")}-end out {ang:0.0} len {e.length:0.0} trim {trim:0.00} off {e.lmOff:+0.00;-0.00} plus {e.lmPlus:0.00} minus {e.lmMinus:0.00} at trim -{eM:0.00}/+{eP:0.00} branch {trims.BranchAt(e, n)}");
                }
                if (trims.patch[n]) sb.Append(CityMeshes.DescribeFan(map, trims, n));
            }
            foreach (var tok in (System.Environment.GetEnvironmentVariable("PSX_LM_EDGES") ?? "").Split(','))
            {
                if (!int.TryParse(tok, out int ei)) continue;
                var e = map.edges[ei];
                sb.AppendLine($"e{ei} '{e.name}' {RoadProfiles.All[e.profile].key} len {e.length:0.0} off {e.lmOff:+0.00;-0.00} plus {e.lmPlus:0.00} minus {e.lmMinus:0.00} trims {trims.atA[ei]:0.0}/{trims.atB[ei]:0.0}");
                if (e.lmEase != null)
                    foreach (var z in e.lmEase) sb.AppendLine($"  ease side {z.side} from {(z.fromA ? "a" : "b")} dw {z.dw:0.00} len {z.len:0.0} d0 {z.d0:0.0} narrow {RoadProfiles.All[z.narrow].key}");
                var lay = LineModel.LayoutOf(e);
                for (int k = 0; k <= 8; k++)
                {
                    float s = e.length * k / 8f;
                    LineModel.Extents(e, s, out float eM, out float eP);
                    LineModel.LinesAt(e, s, la);
                    var line = new StringBuilder();
                    foreach (var l in la) line.Append($" {lay.kind[l.k]}@{l.lat:+0.00;-0.00}");
                    sb.AppendLine($"  s={s:0.0} -{eM:0.00}/+{eP:0.00} lanes {LineModel.LaneCentre(e, s):+0.00;-0.00}:{line}");
                }
                sb.Append(CityMeshes.DescribeSections(map, trims, e));
            }
            // PSX_LM_PTS = "x,z;x,z": build the 3 x 3 tiles round each point
            // (centre last, as the drive audit does) and ask what a ray from
            // 3 m up meets there, back faces included
            var buildings = CityBuildings.Precompute(map);
            foreach (var tok in (System.Environment.GetEnvironmentVariable("PSX_LM_PTS") ?? "").Split(';'))
            {
                var xz = tok.Split(',');
                if (xz.Length < 2 || !float.TryParse(xz[0], out float px) || !float.TryParse(xz[1], out float pz)) continue;
                int tx = Mathf.FloorToInt(px / CityMeshes.TileSize), tz = Mathf.FloorToInt(pz / CityMeshes.TileSize);
                var root = new GameObject("~lmprobe");
                CityMeshes.RecordTap = true;
                CityMeshes.TileMeshes centre = null;
                for (int k = 0; k < 9; k++)
                {
                    int dx = k < 8 ? (k % 3) - 1 : 0, dz = k < 8 ? (k / 3) - 1 : 0;
                    if (k < 8 && dx == 0 && dz == 0) continue;
                    var tm = CityMeshes.Build(map, trims, buildings, tx + dx, tz + dz);
                    if (k == 8) centre = tm;
                    var go = new GameObject($"tile_{tx + dx}_{tz + dz}");
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = tm.origin;
                    CityWorld.Attach(go, tm, null);
                }
                CityMeshes.RecordTap = false;
                Physics.SyncTransforms();
                map.NearestRoadPoint(new Vector2(px, pz), 60f, false, out int ne, out float nat, out _);
                float y0 = ne >= 0 ? map.edges[ne].YAt(nat) : 100f;
                sb.AppendLine($"point ({px:0.0}, {pz:0.0}) tile {tx},{tz} nearest e{ne} s={nat:0.0} y {y0:0.00}");
                foreach (bool back in new[] { false, true })
                {
                    Physics.queriesHitBackfaces = back;
                    var hits = Physics.RaycastAll(new Vector3(px, y0 + 3f, pz), Vector3.down, 6.5f);
                    System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                    foreach (var h in hits)
                        sb.AppendLine($"   {(back ? "back " : "front")} hit {h.collider.transform.parent?.name}/{h.collider.name} y {h.point.y - y0:+0.00;-0.00} normal.y {h.normal.y:0.00} tri {h.triangleIndex}");
                }
                Physics.queriesHitBackfaces = false;
                // the tap spans of the centre tile whose outer quad holds the point
                if (centre?.tap != null && centre.roads != null)
                {
                    var verts = centre.roads.vertices;
                    foreach (var sp in centre.tap.spans)
                    {
                        int bi = -1;
                        for (int i = 0; i < centre.roadSlots.Length; i++) if ((int)centre.roadSlots[i] == sp.slot) bi = centre.tap.slotBase[i];
                        if (bi < 0) continue;
                        int nq = System.Math.Max(1, sp.strips), i0 = bi + sp.bucketV;
                        var AL = verts[i0] + centre.origin; var BL = verts[i0 + 1] + centre.origin;
                        var BR = verts[i0 + 4 * (nq - 1) + 2] + centre.origin; var AR = verts[i0 + 4 * (nq - 1) + 3] + centre.origin;
                        float minx = Mathf.Min(Mathf.Min(AL.x, BL.x), Mathf.Min(BR.x, AR.x)), maxx = Mathf.Max(Mathf.Max(AL.x, BL.x), Mathf.Max(BR.x, AR.x));
                        float minz = Mathf.Min(Mathf.Min(AL.z, BL.z), Mathf.Min(BR.z, AR.z)), maxz = Mathf.Max(Mathf.Max(AL.z, BL.z), Mathf.Max(BR.z, AR.z));
                        if (px < minx - 1f || px > maxx + 1f || pz < minz - 1f || pz > maxz + 1f) continue;
                        sb.AppendLine($"   span e{sp.edge} s {sp.sA:0.0}..{sp.sB:0.0} strips {sp.strips} AL ({AL.x:0.00},{AL.z:0.00},{AL.y - y0:+0.00}) BL ({BL.x:0.00},{BL.z:0.00},{BL.y - y0:+0.00}) BR ({BR.x:0.00},{BR.z:0.00},{BR.y - y0:+0.00}) AR ({AR.x:0.00},{AR.z:0.00},{AR.y - y0:+0.00})");
                        for (int q = 0; q < nq && sp.strips > 0; q++)
                        {
                            int j = i0 + 4 * q;
                            sb.AppendLine($"      strip {q}: ({verts[j].x + centre.origin.x:0.00},{verts[j].z + centre.origin.z:0.00}) ({verts[j + 1].x + centre.origin.x:0.00},{verts[j + 1].z + centre.origin.z:0.00}) ({verts[j + 2].x + centre.origin.x:0.00},{verts[j + 2].z + centre.origin.z:0.00}) ({verts[j + 3].x + centre.origin.x:0.00},{verts[j + 3].z + centre.origin.z:0.00})");
                        }
                    }
                }
                Object.DestroyImmediate(root);
            }
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "lm_probe.txt"), sb.ToString());
            Debug.Log("[LineModelProbe] wrote lm_probe.txt");
        }
    }
}
