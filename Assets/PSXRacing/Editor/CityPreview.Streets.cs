using System.Collections.Generic;
using System.IO;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    public static partial class CityPreview
    {
        /// <summary>
        /// STREETS PASS 4 (2026-10-28): the owner's Tryon Sprint start from
        /// the chase camera (every 100 m of its first 500), the junctions it
        /// passes from above, an uptown crosswalk corner at the windscreen, a
        /// lot from the street and a sidewalk at a walker's eye - each shot
        /// BEFORE (CityMeshes.WalksOn off, PSX_CITY_WALKS=0) and AFTER in one
        /// run, with each view's centre tile's triangles and draws.
        /// PSX_STREETS_ONLY=a,b limits it to the views whose names start so.
        /// Writes Screenshots/City/streets/*_0.png (before) and *_1.png.
        ///   -executeMethod PSXRacing.EditorTools.CityPreview.RunStreets
        /// </summary>
        public static void RunStreets()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[Streets] no city data"); return; }
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "City", "streets");
            Directory.CreateDirectory(dir);
            PSXRacingBuilder.EnsureCityTextures();
            var trims = CityMeshes.NodeTrims(map);
            var buildings = CityBuildings.Precompute(map);
            Shader.SetGlobalFloat("_PSXFogNear", 900f);
            Shader.SetGlobalFloat("_PSXFogFar", 2000f);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f));
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.9f, 0.87f, 0.8f));

            var views = new List<(string name, Vector3 eye, Vector3 look, float ortho, float fov, Vector2 ringAt)>();
            var route = map.RouteById("tryon");
            if (route != null)
            {
                for (int d = 0; d <= 400; d += 100)
                    if (RoutePose(map, route, d, out var pos, out var fwd, out _, out _))
                    {
                        var eye = pos - fwd * 6.2f + Vector3.up * 2.1f;
                        views.Add(($"tryon_chase_{d:000}", eye, pos + Vector3.up * 0.9f + fwd * 22f, 0f, 60f, new Vector2(pos.x, pos.z) + new Vector2(fwd.x, fwd.z) * 60f));
                    }
                // the junctions it passes in its first 520 m, from above
                float acc = 0f;
                for (int k = 0; k < route.edges.Length && k < route.dirs.Length; k++)
                {
                    var e = map.edges[route.edges[k]];
                    int endNode = route.dirs[k] > 0 ? e.b : e.a;
                    acc += e.length;
                    float d = acc - route.startM;
                    if (d < -40f) continue;
                    if (d > 520f) break;
                    if (!trims.patch[endNode]) continue;
                    var np = map.nodes[endNode];
                    float y = map.nodeY[endNode];
                    views.Add(($"tryon_top_{Mathf.Max(0f, d):000}_n{endNode}", new Vector3(np.x, y + 300f, np.y), new Vector3(np.x, y, np.y), 34f, 60f, np));
                }
                // a walker's eye on the right-hand walk 40 m past the start, along the street
                if (RoutePose(map, route, 40f, out var wp, out var wf, out var we, out float ws))
                {
                    var right = new Vector3(wf.z, 0f, -wf.x).normalized;
                    float hw = we.width * 0.5f;
                    var foot = wp - Vector3.up * 0.35f + right * (hw + 1.4f - (we.oneway ? 0f : we.width * 0.25f));
                    views.Add(("tryon_walk_eye", foot + Vector3.up * (CityMeshes.CurbH + 1.6f), foot + Vector3.up * 1.2f + wf * 40f - right * 2f, 0f, 60f, new Vector2(foot.x, foot.z) + new Vector2(wf.x, wf.z) * 30f));
                }
            }
            {
                // uptown: South Tryon 38 m before Trade (the Square's crosswalks), the windscreen
                const float h = 50f * Mathf.Deg2Rad;
                var at = new Vector2(-2338.2f, 4710.4f);
                if (SnapNamed(map, at, "Tryon", out var te, out float tsn))
                {
                    var eye = new Vector3(at.x, te.YAt(tsn) + 1.2f, at.y);
                    var d = new Vector3(Mathf.Sin(h), 0f, Mathf.Cos(h));
                    views.Add(("uptown_square_eye", eye, eye + d * 60f + Vector3.down * 1.0f, 0f, 60f, at + new Vector2(d.x, d.z) * 30f));
                    views.Add(("uptown_square_top", new Vector3(-2316f, te.YAt(tsn) + 300f, 4733f), new Vector3(-2316f, te.YAt(tsn), 4733f), 40f, 60f, new Vector2(-2316f, 4733f)));
                }
                // a suburban shopping centre across its lot from the street
                var ls = new Vector2(-2960f, -1201f);
                if (SnapNamed(map, ls, "", out var le, out float lsn))
                {
                    var eye = new Vector3(ls.x, le.YAt(lsn) + 2.1f, ls.y);
                    views.Add(("lot_street_eye", eye, new Vector3(-2760f, le.YAt(lsn) + 0.4f, -1204f), 0f, 60f, new Vector2(-2860f, -1203f)));
                }
            }
            string only = System.Environment.GetEnvironmentVariable("PSX_STREETS_ONLY");
            var onlyList = string.IsNullOrWhiteSpace(only) ? null : only.Split(',');

            var world = new GameObject("~StreetsWorld");
            var log = new System.Text.StringBuilder("view\tpass\ttile\ttris\tdraws\twalks\n");
            bool was = CityMeshes.WalksOn;
            try
            {
                foreach (var v in views)
                {
                    if (onlyList != null && System.Array.FindIndex(onlyList, o => v.name.StartsWith(o.Trim())) < 0) continue;
                    for (int pass = 0; pass <= 1; pass++)
                    {
                        CityMeshes.WalksOn = pass == 1;
                        CityMeshes.WalkStats.Reset();
                        var tiles = BuildRing(map, trims, buildings, world, v.ringAt, 1, out _);
                        var rot = v.ortho > 0f ? Quaternion.LookRotation(Vector3.down, Vector3.forward) : Quaternion.LookRotation(v.look - v.eye);
                        Shoot(dir, $"{v.name}_{pass}", v.eye, rot, v.ortho, 3000f, 1280, 720, v.fov, 0.2f);
                        int ctx = Mathf.FloorToInt(v.ringAt.x / CityMeshes.TileSize), ctz = Mathf.FloorToInt(v.ringAt.y / CityMeshes.TileSize);
                        foreach (var t in tiles)
                        {
                            if (t == null || t.name != $"~tile_{ctx}_{ctz}") continue;
                            TileCost(t, out int tris, out int draws);
                            log.Append($"{v.name}\t{pass}\t{ctx},{ctz}\t{tris}\t{draws}\t{CityMeshes.WalkStats.Line()}\n");
                        }
                        foreach (var t in tiles) if (t != null) Object.DestroyImmediate(t);
                    }
                }
            }
            finally
            {
                CityMeshes.WalksOn = was;
                Object.DestroyImmediate(world);
            }
            File.WriteAllText(Path.Combine(dir, "streets_cost.txt"), log.ToString());
            Debug.Log("[Streets] " + log.ToString().Replace("\n", " | "));
        }

        /// <summary>The street shots, then the drive audit along the race
        /// routes (CityAudit.RunDrive, city_drive.txt): one Unity start.</summary>
        public static void RunStreetsAndDrive()
        {
            RunStreets();
            CityAudit.RunDrive();
        }

        /// <summary>The tile's own meshes (not its props): triangles and submeshes (draws).</summary>
        static void TileCost(GameObject tile, out int tris, out int draws)
        {
            tris = 0; draws = 0;
            foreach (Transform c in tile.transform)
            {
                var mf = c.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || c.name != mf.sharedMesh.name && c.name != "WBeams") continue;
                var m = mf.sharedMesh;
                for (int s = 0; s < m.subMeshCount; s++) tris += (int)m.GetIndexCount(s) / 3;
                draws += m.subMeshCount;
            }
        }

        /// <summary>A point <paramref name="dist"/> metres past the route's
        /// start, on the right-hand lane of a two-way street, its heading.</summary>
        static bool RoutePose(CityMap map, CityMap.Route r, float dist, out Vector3 pos, out Vector3 fwd, out CityMap.Edge edge, out float sOn)
        {
            pos = default; fwd = Vector3.forward; edge = null; sOn = 0f;
            if (r.edges == null || r.dirs == null) return false;
            float want = r.startM + dist, acc = 0f;
            for (int k = 0; k < r.edges.Length && k < r.dirs.Length; k++)
            {
                var e = map.edges[r.edges[k]];
                if (acc + e.length < want) { acc += e.length; continue; }
                float into = Mathf.Clamp(want - acc, 0f, e.length);
                bool f = r.dirs[k] > 0;
                float s = f ? into : e.length - into;
                Vector2 p = e.PointAt(s), t2 = e.TangentAt(s) * (f ? 1f : -1f);
                fwd = new Vector3(t2.x, 0f, t2.y).normalized;
                var right = new Vector3(t2.y, 0f, -t2.x).normalized;
                float lateral = e.oneway ? 0f : e.width * 0.25f;
                pos = new Vector3(p.x, e.YAt(s) + 0.35f, p.y) + right * lateral;
                edge = e; sOn = s;
                return true;
            }
            return false;
        }
    }
}
