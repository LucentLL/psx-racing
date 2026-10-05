using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    public static partial class CityMeshes
    {
        // ------------------------------------------------------------------
        //  THE FAR SKYLINE'S MESH (Uptown B4, 2026-10-04). The buildings the
        //  skyline shows are emitted by the tile builder's own EmitFootprint -
        //  parts, roof shapes, crowns, the B3 massing, the facade look and
        //  tint - with skylineBuild on: one panel a wall (the tile splits a
        //  wall into panels no wider than 9 m; far off that is triangles for
        //  nothing), no cut off the pavement and no shopfront. So the copy on
        //  the horizon is the building the tile stands up, in the same atlas
        //  column and tint, and swapping one for the other changes nothing
        //  but the triangle count.
        // ------------------------------------------------------------------
        /// <summary>True while the skyline is being emitted.</summary>
        static bool skylineBuild;
        /// <summary>A skyline building's outline keeps no corner that moves
        /// it less than this (metres): a round tower's forty points are a
        /// dozen at 400 m and more, where a metre is half a pixel.</summary>
        const float SkylineSimplifyM = 1.5f;
        /// <summary>How far a skyline building's walls run on below its
        /// ground (owner, 2026-10-04: "I see buildings floating in the sky"
        /// - from I-77 the land under uptown is past the drawn world, so a
        /// base above the visible horizon hung in the sky). 300 m is the
        /// horizon's drop at 5 km past three degrees.</summary>
        const float SkylineSkirtM = 300f;

        /// <summary>Drop, one at a time, the corner nearest the line between
        /// its neighbours while that is under <paramref name="tol"/> metres,
        /// keeping at least four.</summary>
        static void SimplifyPoly(List<Vector2> poly, float tol)
        {
            float tol2 = tol * tol;
            while (poly.Count > 4)
            {
                int n = poly.Count, best = -1;
                float bestD = tol2;
                for (int i = 0; i < n; i++)
                {
                    float d = PtSegDist2(poly[i], poly[(i + n - 1) % n], poly[(i + 1) % n]);
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best < 0) break;
                poly.RemoveAt(best);
            }
        }

        /// <summary>The merged skyline: one mesh, a submesh per material
        /// (the facade atlas, the flat roofs), and where each building's
        /// triangles sit in each submesh's index list, so the skyline can
        /// leave out the buildings the live tiles are drawing.</summary>
        public sealed class SkylineMesh
        {
            public Mesh mesh;
            public Slot[] slots;
            /// <summary>Per submesh: every index, in element order.</summary>
            public int[][] indices;
            /// <summary>Per submesh: element e's indices are
            /// [start[2e], start[2e] + start[2e+1]).</summary>
            public int[][] ranges;
            /// <summary>Per element: its bounds (world space).</summary>
            public Bounds[] bounds;
            public int triangles;
        }

        /// <summary>
        /// Emit the footprints <paramref name="elems"/> (each an outline or a
        /// part, as the tiles draw them) into one mesh in world coordinates.
        /// Finishes any tile job still running first: the builder's scratch
        /// is one build's. Null when nothing was emitted.
        /// </summary>
        public static SkylineMesh BuildSkyline(CityMap map, Trims trims, List<int> elems)
        {
            if (activeJob != null) activeJob.Finish();
            foreach (var b in buckets) b.Clear();
            var tm = new TileMeshes { origin = Vector3.zero };
            var fac = buckets[(int)Slot.FacadeGlass];
            var roof = buckets[(int)Slot.RoofFlat];
            int n = elems.Count;
            var rf = new int[n * 2];
            var rr = new int[n * 2];
            var vf = new int[n * 2];
            var vr = new int[n * 2];
            skylineBuild = true;
            try
            {
                for (int e = 0; e < n; e++)
                {
                    int t0 = fac.t.Count, t1 = roof.t.Count, v0 = fac.Count, v1 = roof.Count;
                    EmitFootprint(map, trims, tm, elems[e]);
                    EndFacade();
                    rf[e * 2] = t0; rf[e * 2 + 1] = fac.t.Count - t0;
                    rr[e * 2] = t1; rr[e * 2 + 1] = roof.t.Count - t1;
                    vf[e * 2] = v0; vf[e * 2 + 1] = fac.Count - v0;
                    vr[e * 2] = v1; vr[e * 2 + 1] = roof.Count - v1;
                }
            }
            finally { skylineBuild = false; EndFacade(); }

            SkylineMesh sm = null;
            int total = fac.Count + roof.Count;
            if (total > 0)
            {
                var mesh = new Mesh { name = "CitySkyline" };
                if (total > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                var verts = new List<Vector3>(total);
                verts.AddRange(fac.v); verts.AddRange(roof.v);
                var uvs = new List<Vector2>(total);
                uvs.AddRange(fac.uv); uvs.AddRange(roof.uv);
                var cols = new List<Color32>(total);
                if (fac.col != null && fac.col.Count == fac.Count) cols.AddRange(fac.col);
                else for (int k = 0; k < fac.Count; k++) cols.Add(new Color32(128, 128, 128, 0));
                for (int k = 0; k < roof.Count; k++) cols.Add(new Color32(128, 128, 128, 0));
                mesh.SetVertices(verts);
                mesh.SetUVs(0, uvs);
                mesh.SetColors(cols);
                var idxF = fac.t.ToArray();
                var idxR = new int[roof.t.Count];
                for (int k = 0; k < idxR.Length; k++) idxR[k] = roof.t[k] + fac.Count;
                mesh.subMeshCount = 2;
                mesh.SetTriangles(idxF, 0, false);
                mesh.SetTriangles(idxR, 1, false);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                mesh.MarkDynamic();
                var bounds = new Bounds[n];
                for (int e = 0; e < n; e++)
                {
                    bool any = false; var b = new Bounds();
                    for (int k = vf[e * 2]; k < vf[e * 2] + vf[e * 2 + 1]; k++) { if (!any) { b = new Bounds(fac.v[k], Vector3.zero); any = true; } else b.Encapsulate(fac.v[k]); }
                    for (int k = vr[e * 2]; k < vr[e * 2] + vr[e * 2 + 1]; k++) { if (!any) { b = new Bounds(roof.v[k], Vector3.zero); any = true; } else b.Encapsulate(roof.v[k]); }
                    bounds[e] = b;
                }
                sm = new SkylineMesh
                {
                    mesh = mesh,
                    slots = new[] { Slot.FacadeGlass, Slot.RoofFlat },
                    indices = new[] { idxF, idxR },
                    ranges = new[] { rf, rr },
                    bounds = bounds,
                    triangles = (idxF.Length + idxR.Length) / 3,
                };
            }
            int stray = 0;
            for (int s = 0; s < buckets.Length; s++)
                if (s != (int)Slot.FacadeGlass && s != (int)Slot.RoofFlat) stray += buckets[s].Count;
            if (sm != null)
            {
                // the heaviest buildings, for the triangle budget
                var order = new List<int>(n);
                for (int e = 0; e < n; e++) order.Add(e);
                order.Sort((a, b) => (rf[b * 2 + 1] + rr[b * 2 + 1]).CompareTo(rf[a * 2 + 1] + rr[a * 2 + 1]));
                var sb = new System.Text.StringBuilder("[CitySkyline] heaviest:");
                for (int k = 0; k < Mathf.Min(8, n); k++)
                {
                    int e = order[k]; var f = map.footprints[elems[e]];
                    sb.Append($" #{elems[e]} {(rf[e * 2 + 1] + rr[e * 2 + 1]) / 3} tris ({f.pts.Length} pts, {f.h:0} m, roof {f.roof}{(f.part ? ", part" : "")})");
                }
                Debug.Log(sb.ToString());
            }
            if (stray > 0) Debug.LogWarning($"[CitySkyline] {stray} vertices landed in other slots and are not drawn");
            foreach (var b in buckets) b.Clear();
            return sm;
        }
    }
}
