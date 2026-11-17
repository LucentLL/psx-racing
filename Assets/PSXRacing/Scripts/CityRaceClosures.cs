using System.Collections.Generic;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing
{
    /// <summary>
    /// THE CLOSED COURSE OF A SANCTIONED EVENT (owner, 2026-11-16 15:10: "I never asked for barricades
    /// or road construction. They can be repurposed for 'legal' race events, like a parade."). In a
    /// SANCTIONED (legal) event raced on a Charlotte route, every road that meets the route - a side
    /// street, an on-ramp, an off-ramp, the crossing road at a turnaround - is shut the way a parade
    /// route is: a row of orange-and-white barricades up it, clear of the course, with a solid barrier
    /// behind them. Never on the race line (each closure walks up its road until its whole span is
    /// clear of the course's pavement; <see cref="Audit"/> checks the line a race drives), never in
    /// free roam, never in a street (illegal) race, never anywhere else: there is no other road
    /// closure or construction dressing in the city. Built by CityTracksRegistrar in a race scene.
    /// PSX_TRACK_CLOSURES unset (DEFAULT, proven 2026-11-16 evening) or =sanctioned: sanctioned events
    /// only (the owner's rule); =0: never; =1: in every city race (the play checks).
    /// </summary>
    public static class CityRaceClosures
    {
        public static bool Enabled => System.Environment.GetEnvironmentVariable("PSX_TRACK_CLOSURES") != "0";

        /// <summary>Does the race being loaded close its course? Only a sanctioned event's
        /// (RaceHandoff.EventKey names an event that is not a street one), unless the switch says.</summary>
        public static bool ForThisRace()
        {
            // RETIRED (owner 2026-11-16 ~21:00: "I'm not a fan of the road construction barriers for races.
            // Drivers can just go around them."): no closure in any race, sanctioned or street, and none in
            // free roam. PSX_TRACK_CLOSURES is ignored. City races become checkpoint races
            // (Checkpoints/CheckpointRace). This file goes once the batch-1 tracks have merged.
            return false;
        }

        /// <summary>How far up a closed road the barricades stand from the junction at least (metres):
        /// past the mouth's curb returns, short of the next junction.</summary>
        const float SetBack = 14f;
        /// <summary>The clear gap kept between a barricade and the course's own pavement (metres).
        /// The owner's 2026-11-16 watched race stopped at barricades standing across the race lanes:
        /// a ramp merging into the course and the freeway beside its C-D road run ON the course's
        /// pavement for tens of metres from their junction, so a barricade 14 m up them stood in its
        /// lanes. Now a closure walks up the closed road until its whole span is this far off the
        /// course, drops any panel that would still reach it, and leaves the road open (logged)
        /// when it runs beside the course to its end.</summary>
        const float Clear = 1.5f;
        const float PanelW = 2.4f, BoardH = 0.2f, LegW = 0.09f, Height = 1.5f;

        static Material boardMat, legMat;

        /// <summary>One closure as built: where its panels stand (for the audit).</summary>
        public struct Placed { public Vector3 a, b; public int edge; }
        /// <summary>What the last <see cref="Build"/> did: its route, the roads shut, those left open
        /// because they run beside the course, and every panel row (a = one end, b = the other).</summary>
        public static string LastRoute;
        public static int LastClosed, LastLeftOpen;
        public static readonly List<Placed> LastPlaced = new List<Placed>();

        /// <summary>The course's pavement, as the route's edges and half-widths: how far a point
        /// stands outside it (negative = on it).</summary>
        class Course
        {
            readonly List<Vector2[]> lines = new List<Vector2[]>();
            readonly List<float> half = new List<float>();
            readonly List<Rect> boxes = new List<Rect>();
            public Course(CityMap map, CityMap.Route route)
            {
                foreach (var ei in route.edges)
                {
                    var e = map.edges[ei];
                    if (e.pts == null || e.pts.Length < 2) continue;
                    var lo = e.pts[0]; var hi = e.pts[0];
                    foreach (var p in e.pts) { lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); }
                    float h = Mathf.Max(3f, e.width * 0.5f);
                    lines.Add(e.pts); half.Add(h);
                    boxes.Add(Rect.MinMaxRect(lo.x - h - 30f, lo.y - h - 30f, hi.x + h + 30f, hi.y + h + 30f));
                }
            }
            /// <summary>Metres from <paramref name="p"/> to the edge of the course's pavement (negative
            /// when <paramref name="p"/> is on it); capped at 30 m.</summary>
            public float Outside(Vector2 p)
            {
                float best = 30f;
                for (int k = 0; k < lines.Count; k++)
                {
                    if (!boxes[k].Contains(p)) continue;
                    var l = lines[k];
                    for (int i = 1; i < l.Length; i++)
                    {
                        float d = DistSeg(p, l[i - 1], l[i]) - half[k];
                        if (d < best) best = d;
                    }
                }
                return best;
            }
        }

        static float DistSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float L = ab.sqrMagnitude;
            float t = L > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / L) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>No closures this race (what the checks read).</summary>
        public static void ClearLast() { LastRoute = null; LastClosed = 0; LastLeftOpen = 0; LastPlaced.Clear(); }

        /// <summary>The clear gap kept between a barricade row and the driven lanes of the race line
        /// the scene actually races (metres): the check (<see cref="Audit"/>) fails under 0.5 m, so a row
        /// stands at least 1 m off them.</summary>
        const float LineClear = 1.0f;

        /// <summary>The closures of <paramref name="route"/>, under a new "Closures" object. Returns
        /// how many roads were shut. With <paramref name="line"/> (the scene's TrackPath, built from the
        /// route by CityMode.Awake: the lanes' centre the race drives) every row also keeps
        /// <see cref="LineClear"/> off its driven lanes, measured as <see cref="Audit"/> measures.</summary>
        public static int Build(CityMap map, CityMap.Route route, Transform parent = null, TrackPath line = null)
        {
            LastRoute = route != null ? route.id : null; LastClosed = 0; LastLeftOpen = 0; LastPlaced.Clear();
            if (!Enabled || map == null || route == null || route.edges == null || route.edges.Length == 0) return 0;
            var onRoute = new HashSet<int>(route.edges);
            var course = new Course(map, route);
            Vector3[] wps = line != null && line.waypoints != null && line.waypoints.Length >= 2 ? line.waypoints : null;
            int legs = wps == null ? 0 : (line.pointToPoint ? wps.Length - 1 : wps.Length);
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
            if (!EnsureMaterials()) return 0;
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
                    float w = Mathf.Max(3f, e.width);
                    // walk up the closed road until the whole barricade is clear of the course
                    bool from = e.a == node;
                    bool placed = false;
                    for (float d = Mathf.Min(SetBack, e.length * 0.5f); d <= e.length - 2f && !placed; d += 2f)
                    {
                        float s = from ? d : e.length - d;
                        Vector2 p = e.PointAt(s), t = e.TangentAt(s).normalized;
                        var across = new Vector2(t.y, -t.x);
                        if (course.Outside(p) < w * 0.5f + Clear) continue;
                        if (course.Outside(p + across * (w * 0.5f)) < Clear || course.Outside(p - across * (w * 0.5f)) < Clear) continue;
                        var at = new Vector3(p.x, e.YAt(s), p.y);
                        var along = new Vector3(t.x, 0f, t.y);
                        if (!Fit(course, at, along, w, out float lo, out float hi, out bool[] stand)) continue;
                        if (wps != null)
                        {
                            var ax = new Vector3(along.z, 0f, -along.x);
                            Vector3 ra = at + ax * lo, rb = at + ax * hi;
                            if (LineMargin(new Vector2(ra.x, ra.z), new Vector2(rb.x, rb.z), wps, legs, line.roadWidth * 0.5f, course, out _) < LineClear) continue;
                        }
                        Shut(root, mesh, at, along, w, lo, hi, stand, ei);
                        placed = true;
                    }
                    if (placed) n++;
                    else { LastLeftOpen++; Debug.Log("[CityTracks] " + route.id + ": " + (e.name ?? "a ramp") + " (edge " + ei + ") runs beside the course to its end: left open"); }
                }
            }
            mesh.Emit(root, "Barricades", boardMat, legMat);
            LastClosed = n;
            Debug.Log("[CityTracks] " + route.id + ": " + n + " roads closed, " + LastLeftOpen + " left open beside the course" +
                      (wps != null ? " (every row " + LineClear.ToString("0.0") + " m or more off the race line's lanes)" : " (no race line given)"));
            return n;
        }

        /// <summary>Which barricade panels across a road of <paramref name="width"/> at <paramref name="at"/>
        /// stand clear of the course (each panel's both ends <see cref="Clear"/> off its pavement), and the
        /// row's span across the road [lo, hi]. False when none can stand.</summary>
        static bool Fit(Course course, Vector3 at, Vector3 along, float width, out float lo, out float hi, out bool[] stand)
        {
            var across = new Vector3(along.z, 0f, -along.x);
            int panels = Mathf.Max(1, Mathf.CeilToInt(width / PanelW));
            float pw = width / panels;
            lo = float.MaxValue; hi = float.MinValue;
            stand = new bool[panels];
            for (int i = 0; i < panels; i++)
            {
                float off = (i + 0.5f) * pw - width * 0.5f;
                var c = at + across * off;
                var ea = c + across * (pw * 0.5f); var eb = c - across * (pw * 0.5f);
                if (course.Outside(new Vector2(ea.x, ea.z)) < Clear || course.Outside(new Vector2(eb.x, eb.z)) < Clear) continue;
                stand[i] = true;
                lo = Mathf.Min(lo, off - pw * 0.5f); hi = Mathf.Max(hi, off + pw * 0.5f);
            }
            return lo <= hi;
        }

        /// <summary>One closure: the standing panels of a fitted row (<see cref="Fit"/>) at
        /// <paramref name="at"/> (the road's surface), boards facing along <paramref name="along"/>, and a
        /// box collider across them.</summary>
        static void Shut(Transform root, MeshAcc mesh, Vector3 at, Vector3 along, float width, float lo, float hi, bool[] stand, int edge)
        {
            var across = new Vector3(along.z, 0f, -along.x);
            int panels = stand.Length;
            float pw = width / panels;
            for (int i = 0; i < panels; i++)
            {
                if (!stand[i]) continue;
                float off = (i + 0.5f) * pw - width * 0.5f;
                var c = at + across * off;
                // two legs, three boards (stripes on the boards' texture)
                mesh.Box(c + across * (pw * 0.5f - 0.15f) + Vector3.up * (Height * 0.5f), across, along, new Vector3(LegW, Height, LegW), false);
                mesh.Box(c - across * (pw * 0.5f - 0.15f) + Vector3.up * (Height * 0.5f), across, along, new Vector3(LegW, Height, LegW), false);
                foreach (float h in new[] { 0.45f, 0.85f, 1.25f })
                    mesh.Box(c + Vector3.up * h, across, along, new Vector3(pw - 0.1f, BoardH, 0.04f), true);
            }
            var mid = at + across * ((lo + hi) * 0.5f);
            var col = new GameObject("Closure").AddComponent<BoxCollider>();
            col.transform.SetParent(root, false);
            col.transform.position = mid + Vector3.up * 0.9f;
            col.transform.rotation = Quaternion.LookRotation(along, Vector3.up);
            col.size = new Vector3(hi - lo, 1.8f, 0.5f);
            LastPlaced.Add(new Placed { a = at + across * lo, b = at + across * hi, edge = edge });
        }

        /// <summary>How far a barricade row (a..b) stands outside the driven lanes of a race line
        /// (metres; negative = on them): its distance to the nearest leg, less the lanes' half-width
        /// there: the race's road (half the TrackPath's roadWidth: the AI uses all of it, on a ramp too),
        /// or the course's own pavement round that leg's waypoint where wider, 3.7 m a side at least. The
        /// owner's second watched run (15:50) had a car hit a row 3.2 m off the line: the lap's C-D road is
        /// narrower than the 18.8 m road its cars drive. The one measure <see cref="Build"/> places by and
        /// <see cref="Audit"/> checks.</summary>
        static float LineMargin(Vector2 a, Vector2 b, Vector3[] w, int legs, float roadHalf, Course course, out float halfLane)
        {
            float best = float.MaxValue;
            int at = 0;
            for (int i = 0; i < legs; i++)
            {
                var p0 = new Vector2(w[i].x, w[i].z);
                var p1 = new Vector2(w[(i + 1) % w.Length].x, w[(i + 1) % w.Length].z);
                float d = SegSeg(a, b, p0, p1);
                if (d < best) { best = d; at = i; }
            }
            halfLane = Mathf.Max(3.7f, roadHalf);
            if (course != null) halfLane = Mathf.Max(halfLane, -course.Outside(new Vector2(w[at].x, w[at].z)));
            return best - halfLane;
        }

        /// <summary>
        /// THE CHECK (the owner's watched race, 2026-11-16): walk the race line a scene actually raced
        /// (its TrackPath, every waypoint, so a reverse twin's is the same road the other way) and count
        /// the closures standing within <paramref name="halfLane"/> + 0.5 m of it - a barricade on the
        /// driven lanes. Also counts any closure on the course's own pavement by the map (the rule
        /// Build placed them by). 0 is the only pass.
        /// </summary>
        public static int Audit(TrackPath path, CityMap map, CityMap.Route route, out string worst)
        {
            worst = "";
            if (path == null || path.waypoints == null || path.waypoints.Length < 2 || LastPlaced.Count == 0) return 0;
            var course = map != null && route != null ? new Course(map, route) : null;
            int bad = 0;
            float worstMargin = float.MaxValue;
            var w = path.waypoints;
            int n = path.pointToPoint ? w.Length - 1 : w.Length;
            foreach (var c in LastPlaced)
            {
                float margin = LineMargin(new Vector2(c.a.x, c.a.z), new Vector2(c.b.x, c.b.z), w, n, path.roadWidth * 0.5f, course, out float halfLane);
                float best = margin + halfLane;
                if (margin < 0.5f) bad++;
                if (margin < worstMargin)
                {
                    worstMargin = margin;
                    worst = "edge " + c.edge + " at (" + c.a.x.ToString("0") + ", " + c.a.z.ToString("0") + "): " + best.ToString("0.0") +
                            " m from the race line, the lanes " + halfLane.ToString("0.0") + " m a side";
                }
            }
            return bad;
        }

        static float SegSeg(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            // segments that cross are 0 apart; otherwise the nearest of the four end distances
            float d1 = Cross(b - a, c - a), d2 = Cross(b - a, d - a), d3 = Cross(d - c, a - c), d4 = Cross(d - c, b - c);
            if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f))) return 0f;
            return Mathf.Min(Mathf.Min(DistSeg(a, c, d), DistSeg(b, c, d)), Mathf.Min(DistSeg(c, a, b), DistSeg(d, a, b)));
        }

        static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        static bool EnsureMaterials()
        {
            if (boardMat != null) return true;
            // PSX/Lit only (always included); no other shader by name: a player strips what it does not list
            var lit = Shader.Find("PSX/Lit");
            if (lit == null) { Debug.LogError("[CityTracks] PSX/Lit missing: the closures are not drawn"); return false; }
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
            return true;
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
