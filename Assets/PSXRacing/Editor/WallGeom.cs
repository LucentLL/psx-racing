using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE WALL SOLIDS, AS THE AUDITS READ THEM.
    ///
    /// Every barrier the builders stand beside a road used to be a chain of
    /// BoxColliders, one per 4 m chord, each overlapping the next. The end
    /// face of box k+1 lies in (or a few centimetres proud of) the very plane
    /// a car slides along when it scrapes the wall, and PhysX drops a contact
    /// on an edge only when that edge is INSIDE one triangle mesh, never
    /// between two colliders: so the scraping car met a face whose normal
    /// pointed straight back down the road and stopped dead (WallScrapeAudit:
    /// 136 to 20 km/h in one 1/60 s step, every 36 m of the quarter mile).
    /// "Invisible barriers while scraping walls that look smooth." Each run of
    /// wall is now ONE closed concave MeshCollider on the Solid layer, under
    /// the names the boxes had — "Wall" (a circuit's or strip's perimeter),
    /// "WallColl" (a stage guard wall), "BankColl" (a cut's face),
    /// "WallTunnel" (a bore's side) — with its traffic face exactly where the
    /// boxes' inner faces stood at every station.
    ///
    /// That broke the rule half the audits were written around: "a concave
    /// mesh collider is a SURFACE — road, ground, deck, forecourt — and
    /// everything that can stop a car is a box, a capsule or a convex hull",
    /// and its corollary, "Collider.ClosestPoint means nothing on a concave
    /// mesh (Unity hands the query point straight back), so skip it". Left as
    /// they were, those tools would have stopped seeing every wall in the game
    /// at once and called every venue clear — or stood a car on a wall's top
    /// and called it the ground. This is what they ask instead:
    ///
    ///   IsWallSolid    one of the builders' wall runs: concave, on the Solid
    ///                  layer, under one of the four names (the contract's own
    ///                  test, and the one D's harness shares);
    ///   IsBarrierMesh  ANY concave mesh on the Solid layer: the wall solids,
    ///                  Charlotte's "Barriers" and "Buildings" tile meshes, a
    ///                  forecourt's collided pieces. A thing a car hits, never
    ///                  a floor: the car's own wheel rays skip the layer for
    ///                  exactly that reason (CarController.suspensionMask);
    ///   IsSurfaceMesh  the old rule, restated so it stays true: a concave mesh
    ///                  OFF the Solid layer is a surface;
    ///   ClosestPoint   exact, over the mesh's own triangles in world space, and
    ///                  the query point itself when it is inside a closed mesh —
    ///                  what Collider.ClosestPoint answers for a box, so a wall
    ///                  solid measures exactly as its boxes did;
    ///   SpanAt / VerticalSpan  how tall the solid is at one station, because a
    ///                  run's world bounds reach the highest stone of the whole
    ///                  run and say nothing about the chord beside you;
    ///   Raycast / SegmentCrosses  two-sided and physics-free, for the tests
    ///                  that must answer in a sandbox whose edit-mode physics
    ///                  scene never populated (LifeSimSelfTest's roadside).
    ///
    /// Triangles are read once per mesh, in world space, and bucketed on an XZ
    /// grid, so a 3000-station stage asking a closest point at every station
    /// touches a handful of triangles each time instead of all of them. Keyed
    /// by the collider and re-read if its mesh or transform changes; a venue's
    /// audit calls <see cref="ClearCache"/> when it opens the next scene.
    /// </summary>
    public static class WallGeom
    {
        /// <summary>The Solid layer (WorldKit.SolidLayer, CityWorld.SolidLayer,
        /// PSXRacingBuilder's own): what every barrier stands on, and what the
        /// suspension rays skip.</summary>
        public const int SolidLayer = 9;

        /// <summary>Metres per grid cell. A wall chord is 4 m and a wall 1.2 m
        /// deep, so a chord's top, bottom and faces land in one to four cells.
        /// </summary>
        const float CellM = 8f;
        /// <summary>Most cells one mesh's grid may hold. A footprint bigger than
        /// that (a circuit's whole perimeter from corner to corner is about a
        /// quarter of it at 8 m) doubles the cell until it fits.</summary>
        const int MaxCells = 1 << 20;
        /// <summary>Vertices within a millimetre are one vertex to the closure
        /// test: a solid built with its faces' corners duplicated (flat
        /// normals) is still closed.</summary>
        const float WeldM = 0.001f;
        /// <summary>How far past a point of its surface a solid's vertical
        /// extent is read (<see cref="SpanAt"/>).</summary>
        const float SpanInsetM = 0.05f;
        /// <summary>Two crossings of a vertical line closer than this are one:
        /// the line went down a shared edge and met both triangles on it.
        /// </summary>
        const float CrossingMergeM = 1e-4f;

        // ------------------------------------------------------------------
        //  What a collider IS
        // ------------------------------------------------------------------

        /// <summary>The builders' names for a run of wall solid (the old box
        /// chords' names, kept so name-based code still finds them).
        /// "WallPortal" is not one: a tunnel mouth's portal is a single box,
        /// not a chain, and stays a box.</summary>
        public static bool IsWallName(string name) =>
            name == "Wall" || name == "WallColl" || name == "BankColl" || name == "WallTunnel";

        /// <summary>One of the builders' wall runs: a concave MeshCollider on
        /// the Solid layer under one of <see cref="IsWallName"/>'s names.
        /// Closed by contract (see <see cref="Watertight"/>).</summary>
        public static bool IsWallSolid(Collider c) =>
            c != null && c is MeshCollider mc && !mc.convex && c.gameObject.layer == SolidLayer && IsWallName(c.name);

        /// <summary>Any concave mesh on the Solid layer — the wall solids, and
        /// Charlotte's "Barriers" (Jersey barriers, bridge rails, retaining
        /// walls) and "Buildings" tile meshes, and a forecourt's pieces. An
        /// obstacle, never a surface: nothing on this layer is a floor to the
        /// car's wheels.</summary>
        public static bool IsBarrierMesh(Collider c) =>
            c != null && c is MeshCollider mc && !mc.convex && c.gameObject.layer == SolidLayer;

        /// <summary>A concave mesh OFF the Solid layer: the road, the ground,
        /// a deck, a shoulder ribbon, a rock top — something a wheel stands on.
        /// This is the old "a concave mesh is a surface" rule with the one
        /// exception that has to be made to it now.</summary>
        public static bool IsSurfaceMesh(Collider c) =>
            c != null && c is MeshCollider mc && !mc.convex && c.gameObject.layer != SolidLayer;

        // ------------------------------------------------------------------
        //  The triangle cache
        // ------------------------------------------------------------------

        /// <summary>One concave mesh collider's triangles, in world space, in
        /// an XZ grid of <see cref="cell"/>-metre cells (CSR buckets: cell k's
        /// triangles are items[start[k] .. start[k+1]]). A triangle is listed
        /// in every cell its XZ box overlaps; <see cref="stamp"/> keeps one
        /// query from testing it twice.</summary>
        sealed class Solid
        {
            public Mesh mesh;
            public Matrix4x4 xf;
            public bool readable;
            public Bounds bounds;
            public Vector3[] a, b, c, n;
            public float cell;
            public int x0, z0, nx, nz;
            public int[] start, items, stamp;
            public int query;
            /// <summary>-1 not yet surveyed, 0 open, 1 closed.</summary>
            public int closed = -1;
            public int unpaired;
            public float volume;
        }

        static readonly Dictionary<MeshCollider, Solid> Cache = new Dictionary<MeshCollider, Solid>();
        static readonly List<float> Crossings = new List<float>(16);

        /// <summary>Forget every mesh read so far. Call it when a new scene
        /// opens: the colliders of the last one are gone, and holding their
        /// triangles is holding a venue's worth of memory for nothing.</summary>
        public static void ClearCache() => Cache.Clear();

        static Solid Get(MeshCollider mc)
        {
            var mesh = mc.sharedMesh;
            if (mesh == null) return null;
            Matrix4x4 xf = mc.transform.localToWorldMatrix;
            if (Cache.TryGetValue(mc, out var s) && s.mesh == mesh && s.xf == xf) return s;
            if (Cache.Count >= 512) Purge();
            s = Build(mc, mesh, xf);
            Cache[mc] = s;
            return s;
        }

        /// <summary>Drop the entries whose collider has been destroyed (a scene
        /// closed under a caller that never cleared the cache).</summary>
        static void Purge()
        {
            List<MeshCollider> dead = null;
            foreach (var kv in Cache)
                if (kv.Key == null) (dead ??= new List<MeshCollider>()).Add(kv.Key);
            if (dead != null) foreach (var k in dead) Cache.Remove(k);
        }

        static Solid Build(MeshCollider mc, Mesh mesh, Matrix4x4 xf)
        {
            var s = new Solid { mesh = mesh, xf = xf, readable = mesh.isReadable };
            if (!s.readable)
            {
                // An imported mesh with Read/Write off cannot be walked. Its
                // collider's own bounds are the honest answer (DrawnWorld in
                // TrackObstacleAudit makes the same call, and says so); the
                // builders' SaveMesh meshes are always readable.
                s.bounds = mc.bounds;
                s.a = new Vector3[0];
                return s;
            }

            var v = mesh.vertices;
            var t = mesh.triangles;
            var w = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) w[i] = xf.MultiplyPoint3x4(v[i]);

            int triCount = t.Length / 3, m = 0;
            s.a = new Vector3[triCount];
            s.b = new Vector3[triCount];
            s.c = new Vector3[triCount];
            s.n = new Vector3[triCount];
            Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int k = 0; k < triCount; k++)
            {
                Vector3 A = w[t[3 * k]], B = w[t[3 * k + 1]], C = w[t[3 * k + 2]];
                Vector3 nrm = Vector3.Cross(B - A, C - A);
                float len = nrm.magnitude;
                // No area, no closest point of its own: a sliver is always
                // beside two real triangles that answer for it.
                if (len < 1e-9f) continue;
                s.a[m] = A; s.b[m] = B; s.c[m] = C; s.n[m] = nrm / len;
                m++;
                lo = Vector3.Min(lo, Vector3.Min(A, Vector3.Min(B, C)));
                hi = Vector3.Max(hi, Vector3.Max(A, Vector3.Max(B, C)));
            }
            if (m < triCount)
            {
                System.Array.Resize(ref s.a, m);
                System.Array.Resize(ref s.b, m);
                System.Array.Resize(ref s.c, m);
                System.Array.Resize(ref s.n, m);
            }
            if (m == 0)
            {
                s.bounds = new Bounds(xf.MultiplyPoint3x4(Vector3.zero), Vector3.zero);
                return s;
            }
            s.bounds = new Bounds();
            s.bounds.SetMinMax(lo, hi);

            float cell = CellM;
            while ((double)(Mathf.FloorToInt((hi.x - lo.x) / cell) + 2) *
                   (Mathf.FloorToInt((hi.z - lo.z) / cell) + 2) > MaxCells)
                cell *= 2f;
            s.cell = cell;
            s.x0 = Mathf.FloorToInt(lo.x / cell);
            s.z0 = Mathf.FloorToInt(lo.z / cell);
            s.nx = Mathf.FloorToInt(hi.x / cell) - s.x0 + 1;
            s.nz = Mathf.FloorToInt(hi.z / cell) - s.z0 + 1;

            s.start = new int[s.nx * s.nz + 1];
            for (int k = 0; k < m; k++)
            {
                TriRect(s, k, out int xa, out int xb, out int za, out int zb);
                for (int z = za; z <= zb; z++)
                    for (int x = xa; x <= xb; x++)
                        s.start[z * s.nx + x + 1]++;
            }
            for (int k = 1; k < s.start.Length; k++) s.start[k] += s.start[k - 1];
            s.items = new int[s.start[s.start.Length - 1]];
            var fill = (int[])s.start.Clone();
            for (int k = 0; k < m; k++)
            {
                TriRect(s, k, out int xa, out int xb, out int za, out int zb);
                for (int z = za; z <= zb; z++)
                    for (int x = xa; x <= xb; x++)
                        s.items[fill[z * s.nx + x]++] = k;
            }
            s.stamp = new int[m];
            return s;
        }

        static void TriRect(Solid s, int k, out int xa, out int xb, out int za, out int zb)
        {
            Vector3 A = s.a[k], B = s.b[k], C = s.c[k];
            xa = Mathf.Clamp(CellX(s, Mathf.Min(A.x, Mathf.Min(B.x, C.x))), 0, s.nx - 1);
            xb = Mathf.Clamp(CellX(s, Mathf.Max(A.x, Mathf.Max(B.x, C.x))), 0, s.nx - 1);
            za = Mathf.Clamp(CellZ(s, Mathf.Min(A.z, Mathf.Min(B.z, C.z))), 0, s.nz - 1);
            zb = Mathf.Clamp(CellZ(s, Mathf.Max(A.z, Mathf.Max(B.z, C.z))), 0, s.nz - 1);
        }

        static int CellX(Solid s, float x) => Mathf.FloorToInt(x / s.cell) - s.x0;
        static int CellZ(Solid s, float z) => Mathf.FloorToInt(z / s.cell) - s.z0;

        static void NextQuery(Solid s)
        {
            if (++s.query == int.MaxValue)
            {
                System.Array.Clear(s.stamp, 0, s.stamp.Length);
                s.query = 1;
            }
        }

        static bool HasTris(Solid s) => s != null && s.readable && s.a != null && s.a.Length > 0;

        // ------------------------------------------------------------------
        //  Queries
        // ------------------------------------------------------------------

        /// <summary>
        /// The world bounds of a collider as this class reads it: for a concave
        /// mesh, its triangles' own box (no physics scene needed); for anything
        /// else, <see cref="Collider.bounds"/>. For a wall solid this is the
        /// WHOLE RUN — kilometres, on a circuit — so it is a cheap reject and
        /// nothing more: the height of the wall beside one station is
        /// <see cref="SpanAt"/>'s answer.
        /// </summary>
        public static Bounds WorldBounds(Collider col)
        {
            if (col is MeshCollider mc && !mc.convex)
            {
                var s = Get(mc);
                if (s != null) return s.bounds;
            }
            return col.bounds;
        }

        /// <summary>The point of the collider nearest <paramref name="p"/>. See
        /// the overload.</summary>
        public static Vector3 ClosestPoint(Collider col, Vector3 p) => ClosestPoint(col, p, out _);

        /// <summary>
        /// The point of the collider nearest <paramref name="p"/>, with the
        /// semantics of <see cref="Collider.ClosestPoint"/> for a solid: the
        /// query point itself when it is INSIDE. For a concave mesh (where
        /// Unity's own answer is always the query point) it is exact — the
        /// nearest point over its triangles (Ericson, Real-Time Collision
        /// Detection 5.1.5), found by widening rings of grid cells until no
        /// unvisited cell can hold anything nearer — and "inside" is asked only
        /// of a mesh that is closed (<see cref="Watertight"/>): an open surface
        /// has no inside. <paramref name="normal"/> is the unit normal of the
        /// triangle the point lies on, AS WOUND (outward on a solid built to
        /// the contract; a caller that needs a side orients it), zero when the
        /// point is inside or the collider is not a concave mesh.
        /// </summary>
        public static Vector3 ClosestPoint(Collider col, Vector3 p, out Vector3 normal)
        {
            normal = Vector3.zero;
            if (col == null) return p;
            if (col is MeshCollider mc && !mc.convex)
            {
                var s = Get(mc);
                if (s == null) return p;
                if (!s.readable) return s.bounds.ClosestPoint(p);
                if (!HasTris(s)) return p;
                if (IsClosed(s) && Contains(s, p)) return p;
                int t = Nearest(s, p, out Vector3 q);
                if (t < 0) return p;
                normal = s.n[t];
                return q;
            }
            return col.ClosestPoint(p);
        }

        /// <summary>
        /// How far the collider reaches up and down the vertical line through
        /// (p.x, p.z): the lowest and highest heights at which that line crosses
        /// its surface. False when the line misses it. For a concave mesh this
        /// is read off its triangles (a wall solid's bottom and top at this
        /// point, not the whole run's); for anything else, its bounds.
        /// </summary>
        public static bool VerticalSpan(Collider col, Vector3 p, out float minY, out float maxY)
        {
            minY = maxY = p.y;
            if (col == null) return false;
            if (col is MeshCollider mc && !mc.convex)
            {
                var s = Get(mc);
                if (s == null) return false;
                if (!s.readable)
                {
                    var bb = s.bounds;
                    minY = bb.min.y; maxY = bb.max.y;
                    return p.x >= bb.min.x && p.x <= bb.max.x && p.z >= bb.min.z && p.z <= bb.max.z;
                }
                VerticalCrossings(s, p.x, p.z, Crossings);
                if (Crossings.Count == 0) return false;
                minY = Crossings[0];
                maxY = Crossings[Crossings.Count - 1];
                return true;
            }
            var b = col.bounds;
            minY = b.min.y; maxY = b.max.y;
            return p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z;
        }

        /// <summary>
        /// The solid's vertical extent AT ONE STATION: read <see
        /// cref="SpanInsetM"/> past a point of its surface (<paramref
        /// name="onSurface"/>, usually a <see cref="ClosestPoint"/>) directly
        /// away from <paramref name="from"/> in plan — into the wall, when
        /// <paramref name="from"/> is the road point it was measured from — or
        /// at the point itself when that is straight over or under <paramref
        /// name="from"/>, or when the inset falls off the footprint (a run's end
        /// cap). What a chord box's own min.y/max.y used to say about the wall
        /// beside one station. False, with the point's height as both, when
        /// neither line meets the solid.
        /// </summary>
        public static bool SpanAt(Collider col, Vector3 onSurface, Vector3 from, out float minY, out float maxY)
        {
            Vector3 d = onSurface - from;
            d.y = 0f;
            if (d.sqrMagnitude > 1e-6f &&
                VerticalSpan(col, onSurface + d.normalized * SpanInsetM, out minY, out maxY))
                return true;
            if (VerticalSpan(col, onSurface, out minY, out maxY)) return true;
            minY = maxY = onSurface.y;
            return false;
        }

        /// <summary>
        /// A ray against ONE collider. For a concave mesh it is TWO-SIDED and
        /// needs no physics scene: the first triangle along the ray whichever
        /// way it is wound — so a ray started inside a wall solid finds its far
        /// face, which a physics raycast (queriesHitBackfaces is off in this
        /// project) never would. Meant for short casts, metres not kilometres:
        /// it tests every triangle bucketed in the cells the cast's XZ box
        /// covers. Anything else goes to <see cref="Collider.Raycast"/>.
        /// </summary>
        public static bool Raycast(Collider col, Vector3 origin, Vector3 dir, float maxDist,
                                   out float dist, out Vector3 normal)
        {
            dist = 0f; normal = Vector3.zero;
            if (col == null || maxDist <= 0f || dir.sqrMagnitude < 1e-12f) return false;
            dir = dir.normalized;
            if (col is MeshCollider mc && !mc.convex)
            {
                var s = Get(mc);
                if (HasTris(s)) return RayTris(s, origin, dir, maxDist, out dist, out normal);
            }
            if (!col.Raycast(new Ray(origin, dir), out RaycastHit hit, maxDist)) return false;
            dist = hit.distance;
            normal = hit.normal;
            return true;
        }

        /// <summary>
        /// Does the segment a-b pass through the collider — cross any face of
        /// it, or lie wholly inside it? A slab test in a box's own frame; for a
        /// concave mesh, a two-sided test against its triangles plus, for a
        /// closed one, "is a inside". Physics-free for both, which is why
        /// LifeSimSelfTest's roadside check can ask it in a sandbox whose
        /// physics scene never populated. Anything else is asked by physics
        /// raycast, which cannot see a segment that starts inside it.
        /// </summary>
        public static bool SegmentCrosses(Collider col, Vector3 a, Vector3 b)
        {
            if (col == null) return false;
            if (col is BoxCollider box) return SegmentCrossesBox(box, a, b);
            Vector3 d = b - a;
            float len = d.magnitude;
            if (col is MeshCollider mc && !mc.convex)
            {
                var s = Get(mc);
                if (s == null) return false;
                if (!s.readable)
                    return s.bounds.Contains(a) ||
                           (len > 1e-6f && s.bounds.IntersectRay(new Ray(a, d / len), out float bt) && bt <= len);
                if (!HasTris(s)) return false;
                if (len > 1e-6f && RayTris(s, a, d / len, len, out _, out _)) return true;
                return IsClosed(s) && Contains(s, a);
            }
            if (len < 1e-6f) return false;
            return col.Raycast(new Ray(a, d / len), out _, len);
        }

        /// <summary>
        /// Is this wall solid what the contract says it is: CLOSED (every edge,
        /// its corners welded to the millimetre, used exactly once each way —
        /// <paramref name="unpaired"/> counts the edges that are not: a hole's
        /// rim, or a triangle wound against its neighbours) and WOUND OUTWARD
        /// (<paramref name="volume"/>, the signed volume in world cubic metres,
        /// positive)? An inside-out solid is invisible to every physics ray
        /// (back faces are not hit) and pushes a car INTO the wall rather than
        /// out of it. False for a mesh that cannot be read.
        /// </summary>
        public static bool Watertight(Collider col, out int unpaired, out float volume)
        {
            unpaired = 0; volume = 0f;
            if (!(col is MeshCollider mc) || mc.convex) return false;
            var s = Get(mc);
            if (s == null || !s.readable) return false;
            IsClosed(s);
            unpaired = s.unpaired;
            volume = s.volume;
            return s.closed == 1;
        }

        // ------------------------------------------------------------------
        //  Internals
        // ------------------------------------------------------------------

        static bool IsClosed(Solid s)
        {
            if (s.closed < 0) Survey(s);
            return s.closed == 1;
        }

        /// <summary>Closure and signed volume, in the mesh's own space (exact
        /// duplicates weld exactly there) and signed by the transform's
        /// handedness.</summary>
        static void Survey(Solid s)
        {
            s.closed = 0;
            if (!s.readable) return;
            var v = s.mesh.vertices;
            var t = s.mesh.triangles;
            var id = new int[v.Length];
            var weld = new Dictionary<Vector3Int, int>(v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                var key = Vector3Int.RoundToInt(v[i] / WeldM);
                if (!weld.TryGetValue(key, out int w)) weld[key] = w = weld.Count;
                id[i] = w;
            }
            // +1 for an edge walked low-to-high id, -1 high-to-low: a closed,
            // consistently wound surface walks every edge once each way.
            var edges = new Dictionary<long, int>(t.Length);
            Vector3 o = s.mesh.bounds.center;
            double vol = 0.0;
            int faces = 0;
            for (int k = 0; k + 2 < t.Length; k += 3)
            {
                int a = id[t[k]], b = id[t[k + 1]], c = id[t[k + 2]];
                if (a == b || b == c || c == a) continue;       // collapsed by the weld
                faces++;
                Walk(edges, a, b); Walk(edges, b, c); Walk(edges, c, a);
                Vector3 pa = v[t[k]] - o, pb = v[t[k + 1]] - o, pc = v[t[k + 2]] - o;
                vol += Vector3.Dot(pa, Vector3.Cross(pb, pc));
            }
            int bad = 0;
            foreach (var kv in edges) bad += Mathf.Abs(kv.Value);
            s.unpaired = bad;
            s.volume = (float)(vol / 6.0) * s.xf.determinant;
            s.closed = faces > 0 && bad == 0 ? 1 : 0;
        }

        static void Walk(Dictionary<long, int> edges, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            edges.TryGetValue(key, out int n);
            edges[key] = n + (a < b ? 1 : -1);
        }

        /// <summary>Parity of a vertical line's crossings above the point: odd
        /// is inside. Only meaningful on a closed mesh.</summary>
        static bool Contains(Solid s, Vector3 p)
        {
            VerticalCrossings(s, p.x, p.z, Crossings);
            int above = 0;
            foreach (float y in Crossings) if (y > p.y) above++;
            return (above & 1) == 1;
        }

        /// <summary>The heights at which the vertical line through (x, z)
        /// crosses the mesh, sorted, with a crossing met twice down a shared
        /// edge or vertex counted once. A vertical triangle (a wall's own
        /// faces) is skipped: the line runs along it, not through it. Only the
        /// one cell is read — every triangle the line can meet has its XZ box
        /// over that point, so it is bucketed there.</summary>
        static void VerticalCrossings(Solid s, float x, float z, List<float> ys)
        {
            ys.Clear();
            if (!HasTris(s)) return;
            int cx = CellX(s, x), cz = CellZ(s, z);
            if (cx < 0 || cz < 0 || cx >= s.nx || cz >= s.nz) return;
            int cell = cz * s.nx + cx;
            for (int k = s.start[cell]; k < s.start[cell + 1]; k++)
            {
                int t = s.items[k];
                Vector3 n = s.n[t];
                if (Mathf.Abs(n.y) < 1e-4f) continue;
                Vector3 a = s.a[t], b = s.b[t], c = s.c[t];
                // In the triangle's plan, relative to the point, edges included.
                float ax = a.x - x, az = a.z - z, bx = b.x - x, bz = b.z - z, qx = c.x - x, qz = c.z - z;
                float e0 = ax * bz - az * bx, e1 = bx * qz - bz * qx, e2 = qx * az - qz * ax;
                const float Eps = 1e-8f;
                bool neg = e0 < -Eps || e1 < -Eps || e2 < -Eps;
                bool pos = e0 > Eps || e1 > Eps || e2 > Eps;
                if (neg && pos) continue;
                ys.Add(a.y - (n.x * (x - a.x) + n.z * (z - a.z)) / n.y);
            }
            if (ys.Count < 2) return;
            ys.Sort();
            int keep = 1;
            for (int i = 1; i < ys.Count; i++)
                if (ys[i] - ys[keep - 1] > CrossingMergeM) ys[keep++] = ys[i];
            ys.RemoveRange(keep, ys.Count - keep);
        }

        /// <summary>The nearest triangle to p and the nearest point on it. Rings
        /// of cells outward from p's own: after rings 0..R every triangle not
        /// yet seen lies wholly outside that square, at least R cells from p,
        /// so the search stops as soon as the best found is nearer than that.
        /// </summary>
        static int Nearest(Solid s, Vector3 p, out Vector3 q)
        {
            q = p;
            NextQuery(s);
            int cx = CellX(s, p.x), cz = CellZ(s, p.z);
            int r0 = Mathf.Max(0, Mathf.Max(Mathf.Max(-cx, cx - (s.nx - 1)), Mathf.Max(-cz, cz - (s.nz - 1))));
            int rMax = Mathf.Max(Mathf.Max(cx, s.nx - 1 - cx), Mathf.Max(cz, s.nz - 1 - cz));
            int best = -1;
            float bestD2 = float.PositiveInfinity;
            for (int r = r0; r <= rMax; r++)
            {
                if (best >= 0)
                {
                    float bound = (r - 1) * s.cell;
                    if (bound > 0f && bound * bound >= bestD2) break;
                }
                int xa = cx - r, xb = cx + r, za = cz - r, zb = cz + r;
                for (int x = Mathf.Max(xa, 0); x <= Mathf.Min(xb, s.nx - 1); x++)
                {
                    if (za >= 0 && za < s.nz) NearestIn(s, x, za, p, ref best, ref bestD2, ref q);
                    if (r > 0 && zb >= 0 && zb < s.nz) NearestIn(s, x, zb, p, ref best, ref bestD2, ref q);
                }
                for (int z = Mathf.Max(za + 1, 0); z <= Mathf.Min(zb - 1, s.nz - 1); z++)
                {
                    if (xa >= 0 && xa < s.nx) NearestIn(s, xa, z, p, ref best, ref bestD2, ref q);
                    if (r > 0 && xb >= 0 && xb < s.nx) NearestIn(s, xb, z, p, ref best, ref bestD2, ref q);
                }
            }
            return best;
        }

        static void NearestIn(Solid s, int x, int z, Vector3 p, ref int best, ref float bestD2, ref Vector3 q)
        {
            int cell = z * s.nx + x;
            for (int k = s.start[cell]; k < s.start[cell + 1]; k++)
            {
                int t = s.items[k];
                if (s.stamp[t] == s.query) continue;
                s.stamp[t] = s.query;
                // Relative to p: a stage stands kilometres out, where the
                // difference of two world coordinates is the precise number.
                Vector3 d = ClosestOnTri(s.a[t] - p, s.b[t] - p, s.c[t] - p);
                float d2 = d.sqrMagnitude;
                if (d2 < bestD2) { bestD2 = d2; best = t; q = p + d; }
            }
        }

        /// <summary>Ericson's closest point on triangle abc to the ORIGIN
        /// (Real-Time Collision Detection 5.1.5, with p = 0): the vertex,
        /// edge or face region the origin projects into.</summary>
        static Vector3 ClosestOnTri(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a;
            float d1 = -Vector3.Dot(ab, a), d2 = -Vector3.Dot(ac, a);
            if (d1 <= 0f && d2 <= 0f) return a;
            float d3 = -Vector3.Dot(ab, b), d4 = -Vector3.Dot(ac, b);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            float d5 = -Vector3.Dot(ab, c), d6 = -Vector3.Dot(ac, c);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }

        /// <summary>The nearest triangle along a short ray, either winding.
        /// </summary>
        static bool RayTris(Solid s, Vector3 o, Vector3 d, float len, out float dist, out Vector3 normal)
        {
            dist = float.PositiveInfinity; normal = Vector3.zero;
            Vector3 e = o + d * len;
            int xa = Mathf.Max(0, CellX(s, Mathf.Min(o.x, e.x))), xb = Mathf.Min(s.nx - 1, CellX(s, Mathf.Max(o.x, e.x)));
            int za = Mathf.Max(0, CellZ(s, Mathf.Min(o.z, e.z))), zb = Mathf.Min(s.nz - 1, CellZ(s, Mathf.Max(o.z, e.z)));
            if (xa > xb || za > zb) return false;
            NextQuery(s);
            int best = -1;
            for (int z = za; z <= zb; z++)
                for (int x = xa; x <= xb; x++)
                {
                    int cell = z * s.nx + x;
                    for (int k = s.start[cell]; k < s.start[cell + 1]; k++)
                    {
                        int t = s.items[k];
                        if (s.stamp[t] == s.query) continue;
                        s.stamp[t] = s.query;
                        if (!RayTri(o, d, s.a[t], s.b[t], s.c[t], out float hit) || hit > len || hit >= dist) continue;
                        dist = hit;
                        best = t;
                    }
                }
            if (best < 0) return false;
            normal = s.n[best];
            return true;
        }

        /// <summary>Möller–Trumbore, two-sided, with a hair of slack on the
        /// barycentric bounds so a ray down the seam between two triangles of
        /// one face is not a hole in it.</summary>
        static bool RayTri(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 pv = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, pv);
            if (Mathf.Abs(det) < 1e-12f) return false;
            float inv = 1f / det;
            Vector3 tv = o - a;
            float u = Vector3.Dot(tv, pv) * inv;
            if (u < -1e-5f || u > 1f + 1e-5f) return false;
            Vector3 qv = Vector3.Cross(tv, e1);
            float v = Vector3.Dot(d, qv) * inv;
            if (v < -1e-5f || u + v > 1f + 1e-5f) return false;
            t = Vector3.Dot(e2, qv) * inv;
            return t >= 0f;
        }

        /// <summary>Does the segment a-b pass through the box? A slab test in
        /// the box's own frame. (LifeSimSelfTest's, moved here so the roadside
        /// check asks one place about both kinds of barrier.)</summary>
        static bool SegmentCrossesBox(BoxCollider box, Vector3 a, Vector3 b)
        {
            var xf = box.transform;
            Vector3 la = xf.InverseTransformPoint(a) - box.center;
            Vector3 d = xf.InverseTransformPoint(b) - box.center - la;
            Vector3 h = box.size * 0.5f;
            float t0 = 0f, t1 = 1f;
            for (int ax = 0; ax < 3; ax++)
            {
                if (Mathf.Abs(d[ax]) < 1e-6f)
                {
                    if (la[ax] < -h[ax] || la[ax] > h[ax]) return false;
                    continue;
                }
                float u0 = (-h[ax] - la[ax]) / d[ax], u1 = (h[ax] - la[ax]) / d[ax];
                if (u0 > u1) { float swap = u0; u0 = u1; u1 = swap; }
                t0 = Mathf.Max(t0, u0);
                t1 = Mathf.Min(t1, u1);
                if (t0 > t1) return false;
            }
            return true;
        }
    }
}
