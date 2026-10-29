using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ------------------------------------------------------------------
    //  CITY-BUILD HITCHES (2026-10-29; the owner's I-277 race frame showed
    //  the HUD's "build max 208.0 ms/10 s"). The tile builder's step
    //  machinery, kept apart from the geometry code so the builders' own
    //  files stay theirs:
    //
    //   - THE ZONE CLOCK: inclusive time and calls of the builder's lazy,
    //     expensive pieces (a road's first sectioning, an outline, a fan's
    //     pavement, a verge solve...), read by Editor/HitchProbe.cs around
    //     each step, so a long step says where its time went. Off (one
    //     branch per call) unless a tool turns it on.
    //   - The step's label (phase, item, part) for the tools.
    // ------------------------------------------------------------------
    public static partial class CityMeshes
    {
        /// <summary>The step under way (or the one that just ran): its phase
        /// (<see cref="PhaseNames"/>), item (an edge, a node, a footprint)
        /// and part, as TileJob.BigSteps records them (the probes).</summary>
        public static int StepPhaseNow => stepPhase;
        public static int StepItemNow => stepItem;
        public static int StepPartNow => stepPart;

        /// <summary>Inclusive time per zone of the builder (outermost call of a
        /// zone only, so a recursive one is not counted twice).</summary>
        public static class HitchClock
        {
            public static bool On;
            public const int RawSections = 0, Outline = 1, RawOutline = 2, FanPoly = 3, LaneExt = 4,
                             SolveStrip = 5, EmitStrip = 6, DropFrom = 7, RailLanes = 8, Gather = 9,
                             Chain = 10, Squeeze = 11, Meet = 12, FanCorners = 13, Unions = 14,
                             InCut = 15, WBeam = 16, Chord = 17, StepNb = 18, Ends = 19, Samples = 20, Count = 21;
            public static readonly string[] Names =
            {
                "raw sections (miss)", "outline (miss)", "raw outline (miss)", "fan pavement (miss)", "lane extents",
                "strip solve", "strip emit", "drop probe", "rail off lanes", "gather pavement",
                "chain", "squeeze", "meet pavement", "fan corners", "unions",
                "cut probe", "w-beam corridor", "chord rail", "step to neighbour", "structure ends", "sample positions",
            };
            public static readonly long[] Ticks = new long[Count];
            public static readonly int[] Calls = new int[Count];
            internal static readonly int[] Depth = new int[Count];
            public static void Reset()
            {
                System.Array.Clear(Ticks, 0, Count);
                System.Array.Clear(Calls, 0, Count);
                System.Array.Clear(Depth, 0, Count);
            }
            public static double Ms(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        // ---- THE FINER STEPS (city-build-hitches) ----------------------------
        // Every switch below only re-cuts the build into more, smaller steps or
        // computes the same numbers sooner or more cheaply: a tile is the same
        // bit for bit either way (Editor/HitchProbe.cs builds every tile along
        // three drives both ways and compares every vertex, normal, UV, colour
        // and index). PSX_CITY_FINESTEPS=0: the steps as they were (the A/B).
        public static bool FineSteps = System.Environment.GetEnvironmentVariable("PSX_CITY_FINESTEPS") != "0";
        /// <summary>MeetPavement's candidate test skips a road (or a segment
        /// of it) too far from both edge vertices to pass it - the same roads
        /// pass, without their line model asked at every segment.</summary>
        public static bool MeetPrune => FineSteps && MeetPruneEnv;
        static readonly bool MeetPruneEnv = System.Environment.GetEnvironmentVariable("PSX_CITY_MEETPRUNE") != "0";
        /// <summary>Before an edge's side flags (the roads phase, where the clip
        /// table is final and an outline is the same whenever it is drawn),
        /// the outlines its probes can reach, one a step.</summary>
        public static bool WarmOutlines => FineSteps && WarmOutlinesEnv;
        static readonly bool WarmOutlinesEnv = System.Environment.GetEnvironmentVariable("PSX_CITY_WARMOUTLINES") != "0";
        /// <summary>A big mesh's normals in chunks of whole parts (no vertex
        /// shared across a cut), each by Unity's own RecalculateNormals.</summary>
        public static bool ChunkNormals => FineSteps && ChunkNormalsEnv;
        static readonly bool ChunkNormalsEnv = System.Environment.GetEnvironmentVariable("PSX_CITY_CHUNKNORMALS") != "0";
        /// <summary>The colliders' PhysX cooking done in the job's own steps
        /// (Physics.BakeMesh), so standing a tile up assigns cooked meshes.</summary>
        public static bool BakeColliders => FineSteps && BakeEnv;
        static readonly bool BakeEnv = System.Environment.GetEnvironmentVariable("PSX_CITY_BAKE") != "0";

        // ---- MeetPavement's candidates, pruned ----
        /// <summary>No road's pavement reaches further off its OSM line than
        /// the widest corridor (CityElevation.MaxCorridorHalf, plus ten metres
        /// of slack): an edge vertex further than this, plus MeetCandidate's
        /// own reach, from a segment cannot meet that segment's edge.</summary>
        const float MeetFarM = CityElevation.MaxCorridorHalf + 10f + MeetReachM + MeetSlackM;
        static CityMap boxMap;
        static Vector4[] edgeBox;

        /// <summary>Both vertices further than <see cref="MeetFarM"/> from the
        /// plan box round every point of the road (so from all its segments).</summary>
        static bool MeetBoxFar(CityMap map, CityMap.Edge o, Vector2 vL, Vector2 vR)
        {
            if (boxMap != map || edgeBox == null || edgeBox.Length != map.edges.Length)
            {
                boxMap = map;
                edgeBox = new Vector4[map.edges.Length];
                for (int i = 0; i < map.edges.Length; i++)
                {
                    var pts = map.edges[i].pts;
                    var b = new Vector4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
                    if (pts != null)
                        foreach (var p in pts) b = new Vector4(Mathf.Min(b.x, p.x), Mathf.Min(b.y, p.y), Mathf.Max(b.z, p.x), Mathf.Max(b.w, p.y));
                    edgeBox[i] = b;
                }
            }
            var bx = edgeBox[o.index];
            return BoxD2(bx, vL) > MeetFarM * MeetFarM && BoxD2(bx, vR) > MeetFarM * MeetFarM;
        }

        static float BoxD2(Vector4 b, Vector2 p)
        {
            float dx = Mathf.Max(0f, Mathf.Max(b.x - p.x, p.x - b.z)), dy = Mathf.Max(0f, Mathf.Max(b.y - p.y, p.y - b.w));
            return dx * dx + dy * dy;
        }

        // ---- a mesh's normals, a chunk a step ----
        const int NormalChunkTris = 12000;
        static readonly List<Vector3> nrmPos = new List<Vector3>(), nrmOut = new List<Vector3>();
        static readonly List<Vector3> nrmChunkV = new List<Vector3>(), nrmChunkN = new List<Vector3>();
        static readonly List<int> nrmIdx = new List<int>(), nrmChunkI = new List<int>(), nrmSufMin = new List<int>();
        static Mesh nrmTemp;

        /// <summary><see cref="FinishMesh"/> in steps: the normals of a run of
        /// whole parts at a time - triangles that share no vertex with any
        /// triangle outside the run - each run's by RecalculateNormals on a
        /// mesh of its own, which sums the same triangles at each vertex in
        /// the same order as the whole mesh's call. A mesh whose submeshes
        /// share vertices, or a small one, takes the one call as before.</summary>
        static IEnumerable<int> FinishMeshSteps(Mesh m)
        {
            if (m == null) yield break;
            int n = m.vertexCount;
            if (!ChunkNormals || n < 3 * NormalChunkTris / 2) { FinishMesh(m); yield break; }
            m.GetVertices(nrmPos);
            nrmOut.Clear();
            for (int v = 0; v < n; v++) nrmOut.Add(Vector3.zero);
            if (nrmTemp == null) nrmTemp = new Mesh { name = "normals chunk", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
            int vStart = 0;   // the next chunk's first vertex (unreferenced ones go with the chunk after them)
            for (int s = 0; s < m.subMeshCount; s++)
            {
                m.GetIndices(nrmIdx, s);
                int nt = nrmIdx.Count / 3;
                if (nt == 0) continue;
                // the lowest vertex any triangle from k on uses
                nrmSufMin.Clear();
                for (int k = 0; k <= nt; k++) nrmSufMin.Add(int.MaxValue);
                for (int k = nt - 1; k >= 0; k--)
                    nrmSufMin[k] = Mathf.Min(nrmSufMin[k + 1], Mathf.Min(nrmIdx[3 * k], Mathf.Min(nrmIdx[3 * k + 1], nrmIdx[3 * k + 2])));
                // a submesh reaching back into vertices an earlier chunk owns:
                // the one call for the whole mesh after all (it writes them all)
                if (nrmSufMin[0] < vStart) { FinishMesh(m); yield break; }
                int start = 0, maxV = -1;
                bool lastSub = true;
                for (int s2 = s + 1; s2 < m.subMeshCount; s2++) if (m.GetSubMesh(s2).indexCount > 0) { lastSub = false; break; }
                for (int k = 0; k < nt; k++)
                {
                    maxV = Mathf.Max(maxV, Mathf.Max(nrmIdx[3 * k], Mathf.Max(nrmIdx[3 * k + 1], nrmIdx[3 * k + 2])));
                    bool end = k == nt - 1;
                    if (!end && (k + 1 - start < NormalChunkTris || maxV >= nrmSufMin[k + 1])) continue;
                    int vEnd = end && lastSub ? n - 1 : maxV;
                    nrmChunkV.Clear();
                    for (int v = vStart; v <= vEnd; v++) nrmChunkV.Add(nrmPos[v]);
                    nrmChunkI.Clear();
                    for (int j = 3 * start; j < 3 * (k + 1); j++) nrmChunkI.Add(nrmIdx[j] - vStart);
                    nrmTemp.Clear();
                    nrmTemp.SetVertices(nrmChunkV);
                    nrmTemp.SetTriangles(nrmChunkI, 0, false);
                    nrmTemp.RecalculateNormals();
                    nrmTemp.GetNormals(nrmChunkN);
                    for (int v = vStart; v <= vEnd; v++) nrmOut[v] = nrmChunkN[v - vStart];
                    vStart = vEnd + 1; start = k + 1;
                    if (!end) yield return 0;
                }
                yield return 0;
            }
            // (a vertex past every triangle is the whole-mesh call's zero too)
            m.SetNormals(nrmOut);
            m.RecalculateBounds();
        }

        // ---- the colliders cooked ahead ----
        static bool cookKnown;
        static MeshColliderCookingOptions cookOpts;
        /// <summary>The cooking a MeshCollider does when its mesh is assigned,
        /// done now: CityWorld.Attach's MeshColliders then take the cooked
        /// data. Read off a real MeshCollider, so the options are its own.</summary>
        static void BakeCollider(Mesh m)
        {
            if (m == null) return;
            if (!cookKnown)
            {
                var g = new GameObject("~cookOptions") { hideFlags = HideFlags.HideAndDontSave };
                cookOpts = g.AddComponent<MeshCollider>().cookingOptions;
                Object.DestroyImmediate(g);
                cookKnown = true;
            }
            Physics.BakeMesh(m.GetEntityId(), false, cookOpts);
        }

        // ---- the outlines an edge's side flags will read, one a step ----
        static readonly HashSet<int> warmSegs = new HashSet<int>(), warmSeen = new HashSet<int>();
        static readonly List<int> warmEdges = new List<int>(64);

        /// <summary>Every road a probe from one of this edge's decided
        /// sections can reach (GatherNear's reach round each section's two
        /// edge vertices, a superset of what DropFrom, the verges and the
        /// rails read), its outline drawn a step each if the tile has not
        /// drawn it yet. Called in the roads phase only: there the clip table,
        /// the gaps and the gores are final, so an outline drawn now is the
        /// one it would be when first read (and a superset only costs time).</summary>
        static IEnumerable<int> WarmOutlinesSteps(CityMap map, Trims trims, CityMap.Edge e, TileMeshes tm, Vector2 min, Vector2 max)
        {
            int n = sections.Count;
            if (!WarmOutlines || n < 2) yield break;
            var lo = min - Vector2.one * FlagReachM; var hi = max + Vector2.one * FlagReachM;
            float r = VergeMaxRunM + RoadsideRules.ToeTuckRunM + CityElevation.MaxCorridorHalf;
            var o = new Vector2(tm.origin.x, tm.origin.z);
            warmSegs.Clear(); warmSeen.Clear(); warmEdges.Clear();
            for (int k = 0; k < n; k++)
            {
                var p = e.PointAt(sections[k].s);
                if (p.x < lo.x || p.x > hi.x || p.y < lo.y || p.y > hi.y) continue;
                var a = new Vector2(sections[k].L.x, sections[k].L.z) + o;
                var b = new Vector2(sections[k].R.x, sections[k].R.z) + o;
                map.EdgeSegsInRect(Vector2.Min(a, b) - Vector2.one * r, Vector2.Max(a, b) + Vector2.one * r, warmSegs);
            }
            foreach (int packed in warmSegs)
            {
                int oi = packed >> 12;
                if (warmSeen.Add(oi)) warmEdges.Add(oi);
            }
            warmEdges.Sort();
            int phase = stepPhase, item = stepItem, part = stepPart;
            for (int w = 0; w < warmEdges.Count; w++)
            {
                int oi = warmEdges[w];
                if (outlines.ContainsKey(oi)) continue;
                yield return 0;
                stepPhase = phase; stepItem = oi; stepPart = 6;
                // its own raw sections and those of the roads it meets first,
                // each a few dozen sections a step, then the outline the same way
                var oe = map.edges[oi];
                foreach (var _ in WarmRaw(map, trims, oe)) yield return 0;
                foreach (var (ai, _n) in MeetArmsOf(map, oe)) foreach (var _ in WarmRaw(map, trims, map.edges[ai])) yield return 0;
                if (clips.TryGetValue(oi, out var cl))
                    foreach (var c in cl) foreach (var H in c.host.edges) foreach (var _ in WarmRaw(map, trims, H)) yield return 0;
                foreach (var _ in OutlineSteps(map, trims, oi, WarmEvery)) yield return 0;
            }
            stepPhase = phase; stepItem = item; stepPart = part;
        }

        /// <summary>How many sections a warm-ahead step works out (a long
        /// freeway edge has hundreds).</summary>
        const int WarmEvery = 24;

        /// <summary>An edge's raw sections ahead, a step every
        /// <see cref="WarmEvery"/>, if the tile does not hold them (RawOutlineOf's
        /// own extent and its rule for an edge too short to have any).</summary>
        static IEnumerable<int> WarmRaw(CityMap map, Trims trims, CityMap.Edge e)
        {
            if (rawSectionCache.ContainsKey(e.index)) yield break;
            float sMin = trims.atA[e.index], sMax = e.length - trims.atB[e.index];
            if (sMax - sMin < 0.6f || (e.a == e.b && e.length < 1f)) yield break;
            foreach (var _ in RawSectionsMissSteps(map, trims, e, sMin, sMax, WarmEvery)) yield return 0;
        }

        /// <summary>One zone's scope: <c>using var _hz = new HitchZone(HitchClock.X);</c>
        /// at the top of the function (after its cache check, for a miss).</summary>
        internal struct HitchZone : System.IDisposable
        {
            readonly int z;
            readonly long t0;   // 0: clock off; -1: nested in the same zone
            public HitchZone(int zone)
            {
                z = zone;
                if (!HitchClock.On) { t0 = 0L; return; }
                t0 = HitchClock.Depth[zone]++ == 0 ? System.Diagnostics.Stopwatch.GetTimestamp() : -1L;
            }
            public void Dispose()
            {
                if (t0 == 0L) return;
                if (t0 > 0L) HitchClock.Ticks[z] += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
                HitchClock.Calls[z]++;
                HitchClock.Depth[z]--;
            }
        }
    }
}
