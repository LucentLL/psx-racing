using System.Collections.Generic;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing
{
    /// <summary>
    /// THE CHECKPOINT RACES' ROUTER (DESIGN_v2 4.1). Lives outside Scripts/City so the city's tile bake is untouched;
    /// it only reads the parsed <see cref="CityMap"/>.
    /// - The graph as directed-edge STATES (state = edge*2 + (travel b->a ? 1 : 0)), successors with their turn angle,
    ///   one-ways respected, a U-turn sharper than 2.6 rad refused (the same edge straight back only at a dead end):
    ///   exactly tools/city/tracks/lib.mjs route()'s legality, so the runtime GPS and the authored routes agree.
    /// - Per gate, two REVERSE FIELDS: D[state] = what it takes from the START of that state to the gate. METRIC =
    ///   plain metres (the ranking ruler: remaining distance, positions); COST = lib.mjs's model (classMul: arterials
    ///   1, residential 1.25, local 2.5, ramps x4; the "fwy" profile counts freeways and ramps 1 and the rest 4; plus
    ///   60 m x (turn / 90 deg)^2), walked downhill for the player's GPS line and every AI's route.
    /// - Every gate SPAN (the route's carriageway and any parallel same-direction one the gate line crosses) seeds
    ///   the field at its own arc, so a car on a collector road is measured to its own span, not round the block.
    /// - A node POTENTIAL per field (the off-graph fallback and a one-way driven backwards).
    /// - Locate: the car on the graph with a HEIGHT test (a deck over a street is not the street), hysteresis, and
    ///   the drivable-but-unmapped fallback (lots, lawns).
    /// Nothing here allocates per frame.
    /// </summary>
    public sealed class CityRouter
    {
        public readonly CityMap map;
        public readonly int NE, NS;
        public readonly float[] len, hStart, hEnd;
        public readonly bool[] legal;
        public readonly int[] sNode, eNode;
        public readonly int[] succStart, succ;
        public readonly float[] succTurn;
        public readonly int[] predStart, pred;
        public readonly float[] predTurn;
        public const float UTurnMax = 2.6f, TurnPen = 60f;

        static CityRouter cached;
        public static CityRouter For(CityMap map)
        {
            if (map == null) return null;
            if (cached != null && cached.map == map) return cached;
            cached = new CityRouter(map);
            return cached;
        }

        static float Hd(Vector2 a, Vector2 b) => Mathf.Atan2(b.y - a.y, b.x - a.x);
        public static float AngDiff(float a, float b)
        {
            float d = b - a;
            while (d > Mathf.PI) d -= 2f * Mathf.PI;
            while (d < -Mathf.PI) d += 2f * Mathf.PI;
            return d;
        }

        CityRouter(CityMap map)
        {
            this.map = map;
            NE = map.edges.Length; NS = NE * 2;
            len = new float[NS]; hStart = new float[NS]; hEnd = new float[NS];
            legal = new bool[NS]; sNode = new int[NS]; eNode = new int[NS];
            for (int i = 0; i < NE; i++)
            {
                var e = map.edges[i];
                var p = e.pts;
                int n = p != null ? p.Length : 0;
                for (int d = 0; d < 2; d++)
                {
                    int st = i * 2 + d;
                    len[st] = e.length;
                    legal[st] = e.a != e.b && n >= 2 && e.length > 0.01f && !(d == 1 && e.oneway);
                    if (n < 2) continue;
                    float h0 = Hd(p[0], p[1]), h1 = Hd(p[n - 2], p[n - 1]);
                    if (d == 0) { hStart[st] = h0; hEnd[st] = h1; sNode[st] = e.a; eNode[st] = e.b; }
                    else { hStart[st] = h1 + Mathf.PI; hEnd[st] = h0 + Mathf.PI; sNode[st] = e.b; eNode[st] = e.a; }
                }
            }
            var sl = new List<int>(NS * 2); var tl = new List<float>(NS * 2);
            succStart = new int[NS + 1];
            for (int st = 0; st < NS; st++)
            {
                succStart[st] = sl.Count;
                if (!legal[st]) continue;
                int n = eNode[st], ei = st >> 1;
                var ne = map.nodeEdges[n];
                int deg = ne.Count;
                for (int k = 0; k < deg; k++)
                {
                    int nj = ne[k];
                    var f = map.edges[nj];
                    if (f.a == f.b) continue;
                    int nst = nj * 2 + (f.a == n ? 0 : 1);
                    if (!legal[nst]) continue;
                    if (nj == ei && deg > 1) continue;
                    float turn = Mathf.Abs(AngDiff(hEnd[st], hStart[nst]));
                    if (turn > UTurnMax && !(nj == ei && deg == 1)) continue;
                    sl.Add(nst); tl.Add(turn);
                }
            }
            succStart[NS] = sl.Count;
            succ = sl.ToArray(); succTurn = tl.ToArray();
            predStart = new int[NS + 1];
            for (int k = 0; k < succ.Length; k++) predStart[succ[k] + 1]++;
            for (int i = 0; i < NS; i++) predStart[i + 1] += predStart[i];
            pred = new int[succ.Length]; predTurn = new float[succ.Length];
            var fill = new int[NS];
            System.Array.Copy(predStart, fill, NS);
            for (int st = 0; st < NS; st++)
                for (int k = succStart[st]; k < succStart[st + 1]; k++)
                {
                    int y = succ[k], at = fill[y]++;
                    pred[at] = st; predTurn[at] = succTurn[k];
                }
            heapK = new float[NS * 6]; heapV = new int[NS * 6];
        }

        /// <summary>lib.mjs classMul: the authoring router's class weights.</summary>
        public static float ClassMul(CityMap.Edge e, bool fwy)
        {
            if (fwy) return e.link || e.cls >= 4 ? 1f : 4f;
            float m = e.cls >= 2 ? 1f : e.cls == 1 ? 1.25f : 2.5f;
            if (e.link) m *= 4f;
            return m;
        }

        public static float TurnCost(float turn) { float q = turn / (Mathf.PI * 0.5f); return TurnPen * q * q; }

        // ------------------------------------------------------------------ fields
        public sealed class Field
        {
            public float[] D;          // from the START of each state to the gate (Infinity: unreachable)
            public float[] seedG;      // per state: the gate's arc along the travel direction, or -1
            public float[] pot;        // per node: min over legal states leaving it of D
            public bool cost;          // the cost model (else metres)
            public bool fwy;
            public float[] mul;        // per state (null = 1: metres)
            public float Mul(int st) => mul != null ? mul[st] : 1f;
        }

        readonly float[] heapK; readonly int[] heapV; int hn;
        void Push(float k, int v)
        {
            int i = hn++;
            heapK[i] = k; heapV[i] = v;
            while (i > 0) { int p = (i - 1) >> 1; if (heapK[p] <= heapK[i]) break; float tk = heapK[p]; int tv = heapV[p]; heapK[p] = heapK[i]; heapV[p] = heapV[i]; heapK[i] = tk; heapV[i] = tv; i = p; }
        }
        void Pop(out float k, out int v)
        {
            k = heapK[0]; v = heapV[0]; hn--;
            if (hn <= 0) return;
            heapK[0] = heapK[hn]; heapV[0] = heapV[hn];
            int i = 0;
            for (;;)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < hn && heapK[l] < heapK[m]) m = l;
                if (r < hn && heapK[r] < heapK[m]) m = r;
                if (m == i) break;
                float tk = heapK[m]; int tv = heapV[m]; heapK[m] = heapK[i]; heapV[m] = heapV[i]; heapK[i] = tk; heapV[i] = tv; i = m;
            }
        }

        float[] mulCity, mulFwy;
        float[] MulTable(bool fwy)
        {
            if (fwy && mulFwy != null) return mulFwy;
            if (!fwy && mulCity != null) return mulCity;
            var m = new float[NS];
            for (int st = 0; st < NS; st++) m[st] = ClassMul(map.edges[st >> 1], fwy);
            if (fwy) mulFwy = m; else mulCity = m;
            return m;
        }

        /// <summary>A gate's seed: the state it stands on and its arc along that state's travel direction.</summary>
        public struct Seed { public int st; public float g; }

        /// <summary>A reverse field to one gate (seeded at every span). costMode = the cost model, else metres.</summary>
        public Field Build(List<Seed> seeds, bool costMode, bool fwy)
        {
            var F = new Field { D = new float[NS], seedG = new float[NS], pot = new float[map.nodes.Length], cost = costMode, fwy = fwy };
            F.mul = costMode ? MulTable(fwy) : null;
            for (int i = 0; i < NS; i++) { F.D[i] = float.PositiveInfinity; F.seedG[i] = -1f; }
            hn = 0;
            foreach (var s in seeds)
            {
                if (s.st < 0 || s.st >= NS || !legal[s.st]) continue;
                float v = s.g * F.Mul(s.st);
                if (v < F.D[s.st]) { F.D[s.st] = v; F.seedG[s.st] = s.g; Push(v, s.st); }
            }
            while (hn > 0)
            {
                Pop(out float d, out int y);
                if (d > F.D[y]) continue;
                for (int k = predStart[y]; k < predStart[y + 1]; k++)
                {
                    int x = pred[k];
                    if (F.seedG[x] >= 0f) continue;
                    float c = len[x] * F.Mul(x) + (costMode ? TurnCost(predTurn[k]) : 0f) + d;
                    if (c < F.D[x]) { F.D[x] = c; Push(c, x); }
                }
            }
            for (int n = 0; n < F.pot.Length; n++) F.pot[n] = float.PositiveInfinity;
            for (int st = 0; st < NS; st++)
            {
                if (!legal[st]) continue;
                int n = sNode[st];
                if (F.D[st] < F.pot[n]) F.pot[n] = F.D[st];
            }
            return F;
        }

        /// <summary>From the END of a state on: the best successor's turn plus its D.</summary>
        public float EndCost(Field F, int st)
        {
            float best = float.PositiveInfinity;
            for (int k = succStart[st]; k < succStart[st + 1]; k++)
            {
                float c = (F.cost ? TurnCost(succTurn[k]) : 0f) + F.D[succ[k]];
                if (c < best) best = c;
            }
            return best;
        }

        /// <summary>What it takes from a position on a state (t metres along its travel direction) to the gate.</summary>
        public float CostFrom(Field F, int st, float t)
        {
            if (st < 0 || st >= NS || !legal[st]) return float.PositiveInfinity;
            float g = F.seedG[st];
            if (g >= 0f && g >= t - 0.01f) return (g - t) * F.Mul(st);
            return Mathf.Max(0f, len[st] - t) * F.Mul(st) + EndCost(F, st);
        }

        /// <summary>
        /// The U-turn-aware value at a spot on an edge: on a two-way edge the better of the two directions (a car can
        /// turn round); on a one-way edge the legal direction, or - driven backwards - the way back to its start node
        /// plus that node's potential. <paramref name="bestDir"/> is the direction the value came from (+1 a->b).
        /// </summary>
        public float ValueAt(Field F, int e, float s, bool backwards, out int bestDir)
        {
            var ed = map.edges[e];
            float L = ed.length;
            s = Mathf.Clamp(s, 0f, L);
            bestDir = 1;
            if (ed.oneway)
            {
                float fwdV = CostFrom(F, e * 2, s);
                if (!backwards) return fwdV;
                float back = s * F.Mul(e * 2) + F.pot[ed.a];
                if (back < fwdV) { bestDir = -1; return back; }
                return fwdV;
            }
            float f = CostFrom(F, e * 2, s), r = CostFrom(F, e * 2 + 1, L - s);
            if (r < f) { bestDir = -1; return r; }
            return f;
        }

        /// <summary>
        /// Walk downhill from (st, t) to the field's gate: the states in order (the first is st, the last the gate's)
        /// and the gate's arc on the last. False when the gate cannot be reached from there.
        /// </summary>
        public bool Descend(Field F, int st, float t, List<int> states, out float lastT, int maxSteps = 4000)
        {
            states.Clear(); lastT = 0f;
            if (st < 0 || st >= NS || !legal[st]) return false;
            states.Add(st);
            float g = F.seedG[st];
            if (g >= 0f && g >= t - 0.01f) { lastT = g; return true; }
            int x = st;
            for (int guard = 0; guard < maxSteps; guard++)
            {
                float best = float.PositiveInfinity; int by = -1;
                for (int k = succStart[x]; k < succStart[x + 1]; k++)
                {
                    float c = (F.cost ? TurnCost(succTurn[k]) : 0f) + F.D[succ[k]];
                    if (c < best) { best = c; by = succ[k]; }
                }
                if (by < 0 || float.IsInfinity(best)) return false;
                states.Add(by);
                if (F.seedG[by] >= 0f) { lastT = F.seedG[by]; return true; }
                x = by;
            }
            return false;
        }

        // ------------------------------------------------------------------ positions on the graph
        /// <summary>Plan point and travel tangent at arc t along a state.</summary>
        public Vector2 PointOn(int st, float t, out Vector2 tangent)
        {
            var e = map.edges[st >> 1];
            float s = (st & 1) != 0 ? e.length - t : t;
            tangent = e.TangentAt(s);
            if ((st & 1) != 0) tangent = -tangent;
            return e.PointAt(s);
        }
        public float SOn(int st, float t) { var e = map.edges[st >> 1]; return (st & 1) != 0 ? e.length - t : t; }
        public float TOn(int st, float s) { var e = map.edges[st >> 1]; return (st & 1) != 0 ? e.length - s : s; }

        /// <summary>Where a car is on the graph (DESIGN_v2 2.3): edge, arc, the plan distance to the OSM line, and
        /// whether it is driving the edge against its points' direction.</summary>
        public struct Loc
        {
            public bool valid;
            public int edge;
            public float s;
            public int dir;          // +1 travelling a->b, -1 b->a (velocity, else the nose)
            public float dist;       // plan metres from the OSM line
            public bool offGraph;    // nothing within the search: a lot, a lawn, a verge
            public Vector3 at;       // the car's position when located
            public float since;      // Time.time the edge was first a better candidate than the kept one
            public int pending;      // that candidate edge
        }

        readonly HashSet<int> segs = new HashSet<int>();

        /// <summary>Nearest road point in plan within <paramref name="r"/> whose road height is within
        /// <paramref name="dy"/> of the car (dy &lt;= 0: any height). Optional: prefer <paramref name="keep"/>
        /// (hysteresis): kept when within <paramref name="keepSlack"/> of the best.</summary>
        public bool Nearest(Vector3 pos, float r, float dy, int keep, float keepSlack, out int edge, out float s, out float dist)
        {
            edge = -1; s = 0f; dist = float.PositiveInfinity;
            var p = new Vector2(pos.x, pos.z);
            segs.Clear();
            map.EdgeSegsNear(p - new Vector2(r, r), p + new Vector2(r, r), segs);
            float keepD = float.PositiveInfinity, keepS = 0f;
            foreach (int packed in segs)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                if (e.pts == null || si + 1 >= e.pts.Length) continue;
                Vector2 a = e.pts[si], b = e.pts[si + 1], ab = b - a;
                float L2 = ab.sqrMagnitude;
                float u = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / L2) : 0f;
                float d = Vector2.Distance(p, a + ab * u);
                if (d > r) continue;
                float sAt = e.s[si] + u * (e.s[si + 1] - e.s[si]);
                if (dy > 0f && Mathf.Abs(pos.y - e.YAt(sAt)) > dy) continue;
                if (ei == keep && d < keepD) { keepD = d; keepS = sAt; }
                if (d < dist) { dist = d; edge = ei; s = sAt; }
            }
            if (keep >= 0 && keepD <= dist + keepSlack) { edge = keep; s = keepS; dist = keepD; }
            return edge >= 0;
        }

        /// <summary>Full locate (10 Hz for the player): 25 m, height within 4 m, hysteresis 4 m / 0.3 s.</summary>
        public void Locate(Vector3 pos, Vector3 vel, Vector3 nose, ref Loc loc)
        {
            int keep = loc.valid && !loc.offGraph ? loc.edge : -1;
            // keep the current edge unless another is 4 m closer, and for 0.3 s
            bool found = Nearest(pos, 25f, 4f, keep, 4f, out int e, out float s, out float d);
            // the next edge across a node the car is driving over: at once (no stale remaining at a node)
            bool adjacent = found && keep >= 0 && e != keep && SharesNode(e, keep);
            if (found && keep >= 0 && e != keep && !adjacent)
            {
                if (loc.pending != e) { loc.pending = e; loc.since = Time.time; }
                if (Time.time - loc.since < 0.3f && Nearest(pos, 25f, 4f, keep, 1e6f, out int ke, out float ks, out float kd) && ke == keep)
                { e = ke; s = ks; d = kd; }
            }
            else loc.pending = -1;
            if (!found)
            {
                loc.offGraph = true;
                loc.at = pos;
                // keep the last edge for the direction; the remaining distance uses the off-graph fallback
                return;
            }
            loc.valid = true; loc.offGraph = false; loc.edge = e; loc.s = s; loc.dist = d; loc.at = pos;
            loc.dir = DirOf(e, s, vel, nose);
        }

        /// <summary>Per frame between full searches: slide along the located edge (two segment projections).</summary>
        public void Slide(Vector3 pos, Vector3 vel, Vector3 nose, ref Loc loc)
        {
            if (!loc.valid || loc.offGraph) return;
            var e = map.edges[loc.edge];
            var p = new Vector2(pos.x, pos.z);
            int si = e.SegmentAt(loc.s, out _);
            float bestD = float.PositiveInfinity, bestS = loc.s;
            for (int k = Mathf.Max(0, si - 1); k <= Mathf.Min(e.pts.Length - 2, si + 1); k++)
            {
                Vector2 a = e.pts[k], b = e.pts[k + 1], ab = b - a;
                float L2 = ab.sqrMagnitude;
                float u = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / L2) : 0f;
                float d = Vector2.Distance(p, a + ab * u);
                if (d < bestD) { bestD = d; bestS = e.s[k] + u * (e.s[k + 1] - e.s[k]); }
            }
            loc.s = bestS; loc.dist = bestD; loc.at = pos;
            loc.dir = DirOf(loc.edge, loc.s, vel, nose);
        }

        public bool SharesNode(int e1, int e2)
        {
            var a = map.edges[e1]; var b = map.edges[e2];
            return a.a == b.a || a.a == b.b || a.b == b.a || a.b == b.b;
        }

        public int DirOf(int e, float s, Vector3 vel, Vector3 nose)
        {
            var ed = map.edges[e];
            var t = ed.TangentAt(s);
            var v = new Vector2(vel.x, vel.z);
            var dir = v.sqrMagnitude > 9f ? v : new Vector2(nose.x, nose.z);
            return Vector2.Dot(dir, t) >= 0f ? 1 : -1;
        }

        /// <summary>
        /// The field's value for a located car (DESIGN_v2 2.3). On the graph: <see cref="ValueAt"/>. Off it: within
        /// 60 m of any edge, the straight line to the nearest road point plus that point's value; beyond, the best of
        /// (straight line to a node within 300 m + that node's potential): a bound that falls as he closes on the road
        /// and the gate, never "moving away" for cutting across a lot toward it.
        /// </summary>
        public float Remaining(Field F, Vector3 pos, ref Loc loc, out int bestDir)
        {
            bestDir = 1;
            if (loc.valid && !loc.offGraph)
            {
                var e = map.edges[loc.edge];
                bool backwards = e.oneway && loc.dir < 0;
                return ValueAt(F, loc.edge, loc.s, backwards, out bestDir);
            }
            if (Nearest(pos, 60f, 8f, -1, 0f, out int ne, out float ns, out float nd))
                return nd + ValueAt(F, ne, ns, false, out bestDir);
            var p = new Vector2(pos.x, pos.z);
            float best = float.PositiveInfinity;
            segs.Clear();
            map.EdgeSegsNear(p - new Vector2(300f, 300f), p + new Vector2(300f, 300f), segs);
            foreach (int packed in segs)
            {
                var ed = map.edges[packed >> 12];
                float da = Vector2.Distance(p, map.nodes[ed.a]) + F.pot[ed.a];
                float db = Vector2.Distance(p, map.nodes[ed.b]) + F.pot[ed.b];
                if (da < best) best = da;
                if (db < best) best = db;
            }
            return best;
        }
    }
}
