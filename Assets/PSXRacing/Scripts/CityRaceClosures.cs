using System.Collections.Generic;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing
{
    /// <summary>
    /// THE CLOSED COURSE: every road that meets a Charlotte track's route - a side street, an on-ramp,
    /// an off-ramp, the crossing road at a turnaround - is shut with a row of orange-and-white
    /// barricades (Type III, the kind a road closure stands) a few metres up it from the junction, with
    /// a solid barrier behind them, so the course is the course and a wrong exit is a wall. Built when a
    /// race scene is raced as one of the tracks of charlotte_tracks.json (CityTracksRegistrar);
    /// the export's own three races are left as they were. PSX_TRACK_CLOSURES=0: no closures.
    /// </summary>
    public static class CityRaceClosures
    {
        public static bool Enabled => System.Environment.GetEnvironmentVariable("PSX_TRACK_CLOSURES") != "0";

        /// <summary>How far up a closed road the barricades stand from the junction (metres): past
        /// the mouth's curb returns and a ramp's gore, short of the next junction.</summary>
        const float SetBack = 14f;
        const float PanelW = 2.4f, BoardH = 0.2f, LegW = 0.09f, Height = 1.5f;

        static Material boardMat, legMat;

        /// <summary>The closures of <paramref name="route"/>, under a new "Closures" object. Returns
        /// how many roads were shut.</summary>
        public static int Build(CityMap map, CityMap.Route route, Transform parent = null)
        {
            if (!Enabled || map == null || route == null || route.edges == null || route.edges.Length == 0) return 0;
            var onRoute = new HashSet<int>(route.edges);
            var nodes = new List<int>();
            for (int i = 0; i < route.edges.Length; i++)
            {
                var e = map.edges[route.edges[i]];
                int end = route.dirs[i] < 0 ? e.a : e.b;
                if (!route.loop && i == route.edges.Length - 1) break;
                nodes.Add(end);
            }
            if (!route.loop && route.edges.Length > 0)
            {
                var e0 = map.edges[route.edges[0]];
                nodes.Insert(0, route.dirs[0] < 0 ? e0.b : e0.a);
            }
            var root = new GameObject("Closures").transform;
            if (parent != null) root.SetParent(parent, false);
            EnsureMaterials();
            var shut = new HashSet<int>();
            var mesh = new MeshAcc();
            int n = 0;
            foreach (var node in nodes)
            {
                if (node < 0 || map.nodeEdges == null || node >= map.nodeEdges.Length || map.nodeEdges[node] == null) continue;
                foreach (var ei in map.nodeEdges[node])
                {
                    if (onRoute.Contains(ei) || !shut.Add(ei)) continue;
                    var e = map.edges[ei];
                    if (e.length < 6f) continue;
                    float d = Mathf.Min(SetBack, e.length * 0.5f);
                    float s = e.a == node ? d : e.length - d;
                    Vector2 p = e.PointAt(s), t = e.TangentAt(s);
                    float y = e.YAt(s);
                    float w = Mathf.Max(3f, e.width);
                    Shut(root, mesh, new Vector3(p.x, y, p.y), new Vector3(t.x, 0f, t.y).normalized, w);
                    n++;
                }
            }
            mesh.Emit(root, "Barricades", boardMat, legMat);
            Debug.Log("[CityTracks] " + route.id + ": " + n + " roads closed");
            return n;
        }

        /// <summary>One closure: barricade panels across the road at <paramref name="at"/> (the road's
        /// surface), boards facing along <paramref name="along"/>, and a box collider across it.</summary>
        static void Shut(Transform root, MeshAcc mesh, Vector3 at, Vector3 along, float width)
        {
            var across = new Vector3(along.z, 0f, -along.x);
            int panels = Mathf.Max(1, Mathf.CeilToInt(width / PanelW));
            float pw = width / panels;
            for (int i = 0; i < panels; i++)
            {
                var c = at + across * ((i + 0.5f) * pw - width * 0.5f);
                // two legs, three boards (stripes on the boards' texture)
                mesh.Box(c + across * (pw * 0.5f - 0.15f) + Vector3.up * (Height * 0.5f), across, along, new Vector3(LegW, Height, LegW), false);
                mesh.Box(c - across * (pw * 0.5f - 0.15f) + Vector3.up * (Height * 0.5f), across, along, new Vector3(LegW, Height, LegW), false);
                foreach (float h in new[] { 0.45f, 0.85f, 1.25f })
                    mesh.Box(c + Vector3.up * h, across, along, new Vector3(pw - 0.1f, BoardH, 0.04f), true);
            }
            var col = new GameObject("Closure").AddComponent<BoxCollider>();
            col.transform.SetParent(root, false);
            col.transform.position = at + Vector3.up * 0.9f;
            col.transform.rotation = Quaternion.LookRotation(along, Vector3.up);
            col.size = new Vector3(width + 0.6f, 1.8f, 0.5f);
        }

        static void EnsureMaterials()
        {
            if (boardMat != null) return;
            var lit = Shader.Find("PSX/Lit");
            if (lit == null) { Debug.LogWarning("[CityTracks] PSX/Lit missing: closures drawn with the default shader"); lit = Shader.Find("Standard"); }
            // orange and white bands, the retroreflective sheeting of a Type III board
            var tex = new Texture2D(32, 8, TextureFormat.RGB24, false) { name = "BarricadeStripes", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var orange = new Color32(236, 104, 24, 255); var white = new Color32(236, 236, 230, 255);
            var px = new Color32[32 * 8];
            for (int y = 0; y < 8; y++) for (int x = 0; x < 32; x++) px[y * 32 + x] = ((x + y) / 8) % 2 == 0 ? orange : white;
            tex.SetPixels32(px); tex.Apply(false, true);
            boardMat = new Material(lit) { name = "Barricade", hideFlags = HideFlags.DontSave, mainTexture = tex };
            boardMat.color = Color.white;
            legMat = new Material(lit) { name = "BarricadeLeg", hideFlags = HideFlags.DontSave };
            legMat.color = new Color(0.82f, 0.82f, 0.80f);
        }

        /// <summary>Boxes gathered into two submeshes (boards, legs), so a closure is one draw.</summary>
        class MeshAcc
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> board = new List<int>(), leg = new List<int>();

            public void Box(Vector3 c, Vector3 x, Vector3 z, Vector3 size, bool isBoard)
            {
                var y = Vector3.up;
                Vector3 hx = x * size.x * 0.5f, hy = y * size.y * 0.5f, hz = z * size.z * 0.5f;
                Vector3[] faceN = { z, -z, x, -x, y, -y };
                foreach (var nrm in faceN)
                {
                    // the face's two in-plane axes
                    Vector3 u = Mathf.Abs(Vector3.Dot(nrm, y)) > 0.5f ? x : (Mathf.Abs(Vector3.Dot(nrm, x)) > 0.5f ? z : x);
                    Vector3 w = Vector3.Cross(nrm, u);
                    float su = Mathf.Abs(Vector3.Dot(u, x)) > 0.5f ? size.x : size.z;
                    float sw = Mathf.Abs(Vector3.Dot(w, y)) > 0.5f ? size.y : (Mathf.Abs(Vector3.Dot(w, x)) > 0.5f ? size.x : size.z);
                    Vector3 fc = c + nrm * (Mathf.Abs(Vector3.Dot(nrm, x)) > 0.5f ? size.x : Mathf.Abs(Vector3.Dot(nrm, y)) > 0.5f ? size.y : size.z) * 0.5f;
                    int i0 = v.Count;
                    v.Add(fc - u * su * 0.5f - w * sw * 0.5f); v.Add(fc - u * su * 0.5f + w * sw * 0.5f);
                    v.Add(fc + u * su * 0.5f + w * sw * 0.5f); v.Add(fc + u * su * 0.5f - w * sw * 0.5f);
                    float ru = isBoard ? su / 0.6f : 1f;   // one stripe pair every 0.6 m along a board
                    uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(0f, 1f)); uv.Add(new Vector2(ru, 1f)); uv.Add(new Vector2(ru, 0f));
                    var list = isBoard ? board : leg;
                    list.Add(i0); list.Add(i0 + 1); list.Add(i0 + 2); list.Add(i0); list.Add(i0 + 2); list.Add(i0 + 3);
                }
            }

            public void Emit(Transform root, string name, Material b, Material l)
            {
                if (v.Count == 0) return;
                var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                m.SetVertices(v); m.SetUVs(0, uv);
                m.subMeshCount = 2;
                m.SetTriangles(board, 0); m.SetTriangles(leg, 1);
                m.RecalculateNormals(); m.RecalculateBounds();
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { b, l };
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
    }
}
