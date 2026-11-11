using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    public static partial class CityMeshes
    {
        // =====================================================================
        //  THE ROAD SLAB (owner 2026-11-11, North Graham Street: "I see through
        //  gaps in the road at every intersection ... I asked for roads to have
        //  real thickness, not be a single pixel layer"). The roads are one-
        //  sided sheets, and a junction's fan stands FanProudM (12 mm) over the
        //  ribbons that end at its mouths. The step between a ribbon's end row
        //  and the fan's mouth edge had nothing in it: driving toward a junction
        //  the eye looked through a 12 mm slot under the fan onto the lattice
        //  10 cm down (pale dirt), a hairline right across the road a car length
        //  ahead. Two sheets meeting at one height with vertices that do not
        //  coincide (a T-junction in the triangles) leave pixel cracks the same
        //  way. The closing pass (Skirts) closed only edges standing SkirtMinM
        //  over the lattice, and none where pavement carries on past.
        //
        //  Every open edge of a paved sheet (the ribbons, the fans, the decks'
        //  concrete, lots in the fans' slot) now carries, in its own bucket and
        //  so its own material and draw call:
        //   - a FACE straight down from the edge to under whatever lies beside
        //     it lower (the fan's step onto the ribbon; a verge 2 cm under; the
        //     land), tucked SlabTuckM under it: the slab's edge;
        //   - a TONGUE where pavement past it stands level with it or higher:
        //     from the edge out under that pavement, falling 1 in 12, so a ray
        //     through any crack at the join lands on asphalt.
        //  Both are drawn with the road's own up normal and the edge's own UV,
        //  so what shows of them shades as the road beside them. Their top edge
        //  is the sheet's own two vertices (bit for bit): no new crack.
        //  The collider: a face lies in the plane under the sheet's edge, the
        //  step a car met there already; a tongue lies under the pavement past
        //  (or, within RiserMinM, a 2 mm wedge over it).
        //  PSX_CITY_ROADSLAB=0: neither (the picture as it was).
        // =====================================================================

        /// <summary>PSX_CITY_ROADSLAB=0: no slab faces or tongues. Settable
        /// (the before/after shots flip it and rebuild the tiles).</summary>
        public static bool RoadSlabOn = System.Environment.GetEnvironmentVariable("PSX_CITY_ROADSLAB") != "0";
        /// <summary>Pavement past an edge within this of its height (or
        /// higher, to <see cref="SeamHiM"/>) is a join: a tongue goes under it.
        /// Lower than this, the edge is a step: a face goes down past it.</summary>
        const float RiserMinM = 0.002f;
        const float SeamHiM = 0.10f;
        /// <summary>How far under the land or pavement beside a face reaches.</summary>
        const float SlabTuckM = 0.03f;
        /// <summary>A face no deeper than this; deeper drops are the closing
        /// pass's (its retaining faces, fascias, foreslopes).</summary>
        const float SlabMaxM = 0.6f;
        /// <summary>The tongue: out under the pavement past, falling 1 in 12;
        /// the short one where that pavement is narrower than the long one.</summary>
        const float TongueRunM = 0.25f, TongueShortM = 0.08f, TongueFall = 1f / 12f;

        /// <summary>The tile's last build: tongues and faces laid, metres, triangles, ms.</summary>
        public static int slabTongues, slabFaces, slabTris; public static float slabTongueM, slabFaceM; public static double slabMs;
        /// <summary>Pieces the closing pass decided (a face, fascia, foreslope,
        /// or a road under): the slab leaves those alone (plan key of the piece's middle).</summary>
        static readonly HashSet<long> slabTaken = new HashSet<long>();
        /// <summary>The slab's vertices per bucket (their normals are set up
        /// after the roads mesh's normals are made), for the build in <see cref="slabFor"/>.</summary>
        static readonly Dictionary<Bucket, List<int>> slabVerts = new Dictionary<Bucket, List<int>>();
        static TileMeshes slabFor;

        static void SlabMark(Bucket bk, int v0)
        {
            if (!slabVerts.TryGetValue(bk, out var l)) slabVerts[bk] = l = new List<int>(256);
            for (int i = v0; i < bk.v.Count; i++) l.Add(i);
        }

        /// <summary>Run by the closing pass after its road faces, while
        /// <see cref="skirtOpen"/> holds the road sheets' open edges (and
        /// <see cref="skirtOpenOwn"/> their own vertices), <see cref="skirtSheets"/>
        /// the road buckets and <see cref="roadGrid"/> their lying triangles.</summary>
        static void RoadSlab(CityMap map, TileMeshes tm)
        {
            slabTongues = slabFaces = slabTris = 0; slabTongueM = slabFaceM = 0f; slabMs = 0;
            foreach (var l in slabVerts.Values) l.Clear();
            slabFor = tm;
            if (!RoadSlabOn) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var o = tm.origin;
            var probes = slabProbe;
            for (int ei = 0; ei < skirtOpen.Count && ei < skirtOpenOwn.Count; ei++)
            {
                var (A0, B0, nrm, bi) = skirtOpen[ei];
                var (ga, gb) = skirtOpenOwn[ei];
                if (bi >= skirtSheets.Count) continue;
                var bk = skirtSheets[bi];
                int la = ga - openSheetOff[bi], lb = gb - openSheetOff[bi];
                if (la < 0 || lb < 0 || la >= bk.v.Count || lb >= bk.v.Count) continue;
                Vector3 A = bk.v[la], B = bk.v[lb];
                Vector2 uA = bk.uv[la], uB = bk.uv[lb];
                float len = Vector2.Distance(new Vector2(A.x, A.z), new Vector2(B.x, B.z));
                if (len < 0.02f) continue;
                // the closing pass's own pieces (2 m) along it: a face it laid stays its
                int pieces = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(new Vector2(A0.x, A0.z), new Vector2(B0.x, B0.z)) / SkirtStepM));
                bool taken = false;
                for (int pc = 0; pc < pieces && !taken; pc++)
                    taken = slabTaken.Contains(KeyXZ(Vector3.Lerp(A0, B0, (pc + 0.5f) / pieces)));   // as that pass cut it (welded corners)
                if (taken) continue;

                // what lies beside it, at five points along it
                bool level = false, levelFar = true, lower = false;
                float lowest = float.PositiveInfinity, top = Mathf.Max(A.y, B.y);
                bool under = false;
                for (int k = 0; k < probes.Length; k++)
                {
                    var q = Vector3.Lerp(A, B, probes[k]);
                    float n1x = q.x + nrm.x * 0.05f, n1z = q.z + nrm.y * 0.05f;
                    float n3x = q.x + nrm.x * 0.3f, n3z = q.z + nrm.y * 0.3f;
                    bool lv = roadGrid.Between(n1x, n1z, q.y - RiserMinM, q.y + SeamHiM, true) > float.NegativeInfinity;
                    bool lvFar = roadGrid.Between(n3x, n3z, q.y - RiserMinM, q.y + SeamHiM, true) > float.NegativeInfinity;
                    if (k > 0 && k < probes.Length - 1) { level |= lv; if (lv && !lvFar) levelFar = false; }
                    if (lv) continue;
                    // the land beside: the lattice, the ground's sheets, lower pavement
                    float land = LatticeY(map, n1x + o.x, n1z + o.z);
                    land = Mathf.Max(land, groundGrid.Between(n1x, n1z, land, q.y + 0.3f));
                    land = Mathf.Max(land, roadGrid.Between(n1x, n1z, land, q.y + 0.3f));
                    if (land < q.y - RiserMinM) { lower = true; lowest = Mathf.Min(lowest, land); }
                    // a lower road reaching in under the sheet: a face would stand in its lanes
                    if (roadGrid.Between(q.x - nrm.x * 0.1f, q.z - nrm.y * 0.1f, q.y - SlabMaxM - 0.1f, q.y - 0.004f, true) > float.NegativeInfinity) under = true;
                }
                if (under) continue;
                if (lower && top - lowest <= SlabMaxM)
                {
                    float bot = lowest - SlabTuckM;
                    // a kerb face drawn along it already (a fan's chord): from its foot
                    bool kerb = skirtKerb.Contains(KeyXZ(A)) && skirtKerb.Contains(KeyXZ(B));
                    float yA = kerb ? A.y - KerbFaceM : A.y, yB = kerb ? B.y - KerbFaceM : B.y;
                    if (Mathf.Min(yA, yB) > bot + 0.005f)
                    {
                        int v0 = bk.v.Count;
                        var Ab = new Vector3(A.x, bot, A.z); var Bb = new Vector3(B.x, bot, B.z);
                        var At = kerb ? new Vector3(A.x, yA, A.z) : A; var Bt = kerb ? new Vector3(B.x, yB, B.z) : B;
                        bk.Face(Ab, At, Bt, Bb, new Vector3(nrm.x, 0f, nrm.y), uA, uA, uB, uB);
                        SlabMark(bk, v0);
                        slabFaces++; slabFaceM += len; slabTris += 2;
                    }
                }
                if (level)
                {
                    float run = levelFar ? TongueRunM : TongueShortM;
                    var off = new Vector3(nrm.x * run, -run * TongueFall, nrm.y * run);
                    int v0 = bk.v.Count;
                    // facing up and out (the side a ray through the join comes from above)
                    bk.Face(A, A + off, B + off, B, new Vector3(nrm.x * TongueFall, 1f, nrm.y * TongueFall), uA, uA, uB, uB);
                    SlabMark(bk, v0);
                    slabTongues++; slabTongueM += len; slabTris += 2;
                }
            }
            slabMs = clock.Elapsed.TotalMilliseconds;
        }
        static readonly float[] slabProbe = { 0.02f, 0.25f, 0.5f, 0.75f, 0.98f };

        /// <summary>After the roads mesh's normals: the slab's vertices take
        /// the road's up normal, so a face or tongue shades as the pavement
        /// beside it (a 12 mm face lit as a wall was a line of its own).</summary>
        static void SlabNormals(TileMeshes tm, Slot[] roadSlots)
        {
            if (slabFor != tm || tm.roads == null || roadSlots == null) return;
            bool any = false;
            foreach (var l in slabVerts.Values) if (l.Count > 0) { any = true; break; }
            if (!any) return;
            var nrm = new List<Vector3>(tm.roads.vertexCount);
            tm.roads.GetNormals(nrm);
            if (nrm.Count != tm.roads.vertexCount) return;
            int baseV = 0;
            foreach (var s in roadSlots)
            {
                var bk = buckets[(int)s];
                if (slabVerts.TryGetValue(bk, out var l))
                    foreach (int i in l)
                        if (baseV + i < nrm.Count) nrm[baseV + i] = Vector3.up;
                baseV += bk.Count;
            }
            tm.roads.SetNormals(nrm);
        }
    }
}
