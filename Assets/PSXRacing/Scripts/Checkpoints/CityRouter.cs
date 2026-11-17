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
    /// - Per gate, ONE REVERSE FIELD (cpfix 11-17; stage 1 built a second, plain-metre one): D[state] = what it
    ///   takes from the START of that state to the gate in lib.mjs's COST model (classMul: arterials 1, residential
    ///   1.25, local 2.5, ramps x4; the "fwy" profile counts freeways and ramps 1 and the rest 4; plus 60 m x
    ///   (turn / 90 deg)^2), walked downhill for the player's GPS line and every AI's route; and RM[state] = the
    ///   METRES along that same route (the ranking ruler: remaining distance, positions). One route for the line, the
    ///   AI and the ruler: following the GPS the remaining falls metre for metre.
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
        readonly float[] predTurnCost;
        public const float UTurnMax = 2.6f, TurnPen = 60f;
        /// <summary>What a mid-block U-turn costs the ruler's held direction on a two-way street (cost-metres: the
        /// turn model's 180 degrees is 240; half of it, so a clearly better way back still wins).</summary>
        public const float UTurnHyst = 120f;

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
            pred = new int[succ.Length]; predTurn = new float[succ.Length]; predTurnCost = new float[succ.Length];
            var fill = new int[NS];
            System.Array.Copy(predStart, fill, NS);
            for (int st = 0; st < NS; st++)
                for (int k = succStart[st]; k < succStart[st + 1]; k++)
                {
                    int y = succ[k], at = fill[y]++;
                    pred[at] = st; predTurn[at] = succTurn[k]; predTurnCost[at] = TurnCost(succTurn[k]);
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
            /// <summary>THE RULER (cpfix 11-17): per state, the METRES from its start to the gate ALONG THIS FIELD'S
            /// OWN DESCENT (for a cost field: the route the GPS line draws and the AI drive). Following that route the
            /// remaining falls metre for metre and no node steps it (the 01:23 check race: a player on the GPS line
            /// over Todd Rd saw the plain-metre field rise 8,319 -> 8,434 m and jump +602 m at a node).</summary>
            public float[] RM;
            /// <summary>Per state, the successor the descent takes at its end (-1: none). Descend walks it, so the GPS
            /// line, the AI's road and <see cref="RM"/> are one route.</summary>
            public int[] nxt;
            /// <summary>Per node: <see cref="RM"/> of the cost-best state leaving it (the off-graph bound).</summary>
            public float[] potM;
        }

        readonly float[] heapK; readonly int[] heapV; int hn;
        int[] popOrder; int popN;
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

        /// <summary>Per move into a state (pred order): the weight of the predecessor state plus the turn, per mode
        /// (0 metres, 1 city cost, 2 freeway cost), so a relaxation is one add.</summary>
        readonly float[][] predW = new float[3][];
        float[] PredW(bool costMode, bool fwy)
        {
            int m = !costMode ? 0 : fwy ? 2 : 1;
            if (predW[m] != null) return predW[m];
            var w = new float[pred.Length];
            var mul = costMode ? MulTable(fwy) : null;
            for (int k = 0; k < pred.Length; k++)
            {
                int x = pred[k];
                w[k] = mul != null ? len[x] * mul[x] + predTurnCost[k] : len[x];
            }
            predW[m] = w;
            return w;
        }

        /// <summary>A reverse field to one gate (seeded at every span). costMode = the cost model, else metres.
        /// One search: each state's successor on the descent is the move that last improved it (its parent toward
        /// the gate), and the metres along the descent are summed in the order the states settled (cpfix 11-17:
        /// one field, not two, and no second pass over the moves: 19.8 ms a gate in the editor before).</summary>
        public Field Build(List<Seed> seeds, bool costMode, bool fwy)
        {
            int NN = map.nodes.Length;
            var F = new Field { D = new float[NS], seedG = new float[NS], pot = new float[NN], cost = costMode, fwy = fwy,
                                RM = new float[NS], nxt = new int[NS], potM = new float[NN] };
            F.mul = costMode ? MulTable(fwy) : null;
            System.Array.Fill(F.D, float.PositiveInfinity);
            System.Array.Fill(F.seedG, -1f);
            System.Array.Fill(F.RM, float.PositiveInfinity);
            System.Array.Fill(F.nxt, -1);
            System.Array.Fill(F.pot, float.PositiveInfinity);
            System.Array.Fill(F.potM, float.PositiveInfinity);
            if (popOrder == null) popOrder = new int[NS];
            var W = PredW(costMode, fwy);
            hn = 0; popN = 0;
            foreach (var s in seeds)
            {
                if (s.st < 0 || s.st >= NS || !legal[s.st]) continue;
                float v = s.g * F.Mul(s.st);
                if (v < F.D[s.st]) { F.D[s.st] = v; F.seedG[s.st] = s.g; Push(v, s.st); }
            }
            var D = F.D; var sg = F.seedG; var nx = F.nxt;
            int[] pr = pred, ps = predStart;
            while (hn > 0)
            {
                Pop(out float d, out int y);
                if (d > D[y]) continue;
                popOrder[popN++] = y;
                int k1 = ps[y + 1];
                for (int k = ps[y]; k < k1; k++)
                {
                    int x = pr[k];
                    float c = W[k] + d;
                    if (c < D[x] && sg[x] < 0f) { D[x] = c; nx[x] = y; Push(c, x); }
                }
            }
            // a gate's own states: past the gate line the car goes on by the best successor
            bool cm = F.cost;
            foreach (var s in seeds)
            {
                int x = s.st;
                if (x < 0 || x >= NS || sg[x] < 0f || nx[x] >= 0) continue;
                float best = float.PositiveInfinity; int by = -1;
                for (int k = succStart[x]; k < succStart[x + 1]; k++)
                {
                    float c = (cm ? TurnCost(succTurn[k]) : 0f) + D[succ[k]];
                    if (c < best) { best = c; by = succ[k]; }
                }
                nx[x] = float.IsInfinity(best) ? -1 : by;
            }
            // THE METRES ALONG THE DESCENT, in the order the states settled (a parent settles before its child), and
            // each node's potential: the first state to settle leaving it is its cheapest
            var RM = F.RM; var pot = F.pot; var potM = F.potM; var sn = sNode;
            for (int i = 0; i < popN; i++)
            {
                int x = popOrder[i];
                if (sg[x] >= 0f) RM[x] = sg[x];
                else { int y = nx[x]; if (y >= 0) RM[x] = len[x] + RM[y]; }
                int n = sn[x];
                if (float.IsInfinity(pot[n])) { pot[n] = D[x]; potM[n] = RM[x]; }
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

        /// <summary>THE RULER from a position on a state (t metres along its travel direction): metres to the gate
        /// along the field's own descent (<see cref="Field.RM"/>).</summary>
        public float RouteFrom(Field F, int st, float t)
        {
            if (st < 0 || st >= NS || !legal[st]) return float.PositiveInfinity;
            float g = F.seedG[st];
            if (g >= 0f && g >= t - 0.01f) return g - t;
            int y = F.nxt[st];
            return y < 0 ? float.PositiveInfinity : Mathf.Max(0f, len[st] - t) + F.RM[y];
        }

        /// <summary>
        /// THE RULER at a spot on an edge (cpfix 11-17): metres to the gate along the cost-optimal route, the one the
        /// GPS line draws. A TWO-WAY edge: the direction the cost prefers (the GPS's own choice, so a car following
        /// the line sees its remaining fall metre for metre); <paramref name="dir"/> (+1 a->b, -1 b->a, 0 none yet) is
        /// held unless the other way is cheaper by more than <paramref name="hyst"/> (a mid-block U-turn is not free:
        /// <see cref="UTurnHyst"/>); a fresh one (0) starts the way the car travels (<paramref name="travelDir"/>,
        /// 0 = by the cost alone). A ONE-WAY edge: its legal direction only. No backwards credit: a car driven
        /// against it sees its remaining rise (WRONG WAY), and the value never flips with a spinning car's heading
        /// (the 01:23 check race's crashed Supra: +-44-68 m swings).
        /// </summary>
        public float RouteRemaining(Field F, int e, float s, ref int dir, float hyst, int travelDir = 0)
        {
            var ed = map.edges[e];
            float L = ed.length;
            s = Mathf.Clamp(s, 0f, L);
            if (ed.oneway) { dir = 1; return RouteFrom(F, e * 2, s); }
            float cf = CostFrom(F, e * 2, s), cr = CostFrom(F, e * 2 + 1, L - s);
            int want = cr < cf ? -1 : 1;
            if (dir == 0 && (travelDir == 1 || travelDir == -1)) dir = travelDir;
            if ((dir == 1 || dir == -1) && dir != want)
            {
                float held = dir == 1 ? cf : cr, other = dir == 1 ? cr : cf;
                if (!float.IsInfinity(held) && other >= held - hyst) want = dir;
            }
            dir = want;
            return want == 1 ? RouteFrom(F, e * 2, s) : RouteFrom(F, e * 2 + 1, L - s);
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
                // the field's own successor (Build): the same route its RM metres were summed along
                int by = F.nxt[x];
                if (by < 0) return false;
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

        /// <summary>How far a located car may stray from its kept edge (a crash, a lawn) before it is let go.</summary>
        public const float KeepMaxM = 80f;

        /// <summary>
        /// Full locate (10 Hz), STICKY (cpfix 11-17). The 01:23 check race: a Supra knocked 23.5 m off the Wilkinson
        /// loop ramp was taken by whatever edge happened to be 4 m nearer, 190 m behind its own station, and its
        /// remaining jumped 99 -> 289 m. Now:
        /// - the KEPT edge holds while the car is within 80 m of it and on its level (8 m), measured at the car's own
        ///   projection onto it (a car off the road keeps its place along the road it left);
        /// - across a NODE (an edge sharing one with the kept edge, the car on its pavement: half width + 1.5 m):
        ///   at once, by a heading-aware score, so a turn never leaves a stale remaining;
        /// - any OTHER edge only when the car is ON its pavement (half width + 1.5 m) and either off the kept edge's
        ///   pavement or 4 m nearer by the score, for 0.3 s;
        /// - a car with no kept edge takes the best candidate within 25 m and 4 m of height; none: off the graph.
        /// <paramref name="now"/> is the clock the hysteresis counts in (Time.time in play; the self-test's own).
        /// </summary>
        public void Locate(Vector3 pos, Vector3 vel, Vector3 nose, ref Loc loc) => Locate(pos, vel, nose, ref loc, Time.time);

        /// <param name="prefer">The edge the car's route takes next (-1: none): where two roads overlap (a ramp
        /// split's gore, a collector beside the main line) the route's one wins while the car is on its pavement.
        /// The self-test's off-route walks read the sibling ramp at splits for 50-100 m.</param>
        public void Locate(Vector3 pos, Vector3 vel, Vector3 nose, ref Loc loc, float now, int prefer = -1)
        {
            int keep = loc.valid && !loc.offGraph ? loc.edge : -1;
            bool found = NearestScored(pos, vel, 25f, 4f, prefer, keep < 0, out int e, out float s, out float d, out float score);
            // the kept edge is followed along its own arc from where the car was (a window that grows with the
            // distance moved): inside a loop ramp the edge's far side never steals the projection
            float win = 30f + 1.5f * new Vector2(pos.x - loc.at.x, pos.z - loc.at.z).magnitude;
            if (keep >= 0 && Project(keep, pos, loc.s, win, out float ks, out float kd) && kd <= KeepMaxM &&
                Mathf.Abs(pos.y - map.edges[keep].YAt(ks)) <= 8f)
            {
                var ke = map.edges[keep];
                bool onKeep = kd <= ke.HalfMax + 2.5f;
                bool take = false;
                // AT A NODE, THE ROUTE'S ROAD: the car has run off the end of its kept edge and the road the route
                // takes from there is under it (its pavement + 3 m): that one, whatever is a metre nearer (a gore
                // where a ramp leaves a 4-lane main line: both OSM lines lie under the car for 100 m)
                if (prefer >= 0 && prefer != keep && (ks <= 0.5f || ks >= ke.length - 0.5f) && SharesNode(prefer, keep))
                {
                    var pe = map.edges[prefer];
                    bool atA = pe.a == ke.a || pe.a == ke.b;
                    if (Project(prefer, pos, atA ? 0f : pe.length, 60f, out float ps, out float pd) &&
                        pd <= pe.HalfMax + 3f && Mathf.Abs(pos.y - pe.YAt(ps)) <= 4f)
                    { e = prefer; s = ps; d = pd; take = true; loc.pending = -1; }
                }
                if (take) { }
                else if (found && e != keep)
                {
                    var ne = map.edges[e];
                    bool onNew = d <= ne.HalfMax + 1.5f;
                    float kScore = kd + HeadPen(keep, ks, vel);
                    // across a node only onto the new edge's pavement (the self-test's crash walk: a car coming
                    // back from 22 m off was handed to a ramp sharing the node, 125 m further from the gate)
                    // A node's next edge (the route's: 0.5 m better; another: 2 m better, or the car off the kept
                    // pavement), so two roads leaving one node side by side do not swap on a lane's width
                    // ...and a road the route does not take only once the car is off the kept one's pavement: on the
                    // I-485 exit ramp the car drives right over the MAIN LINE's OSM line (0.3 m) for 100 m while the
                    // ramp's own line is 2.5 m away (the 02:15 and 02:27 H2 races: +19 km for a second)
                    if (onNew && SharesNode(e, keep))
                    { take = e == prefer ? score + 0.5f < kScore : (!onKeep && score < kScore); loc.pending = -1; }
                    else if (onNew && (!onKeep || score + 4f < kScore))
                    {
                        if (loc.pending != e) { loc.pending = e; loc.since = now; }
                        take = now - loc.since >= 0.3f;
                    }
                    else loc.pending = -1;
                }
                else loc.pending = -1;
                if (!take) { e = keep; s = ks; d = kd; }
                loc.valid = true; loc.offGraph = false; loc.edge = e; loc.s = s; loc.dist = d; loc.at = pos;
                loc.dir = DirOf(e, s, vel, nose);
                return;
            }
            loc.pending = -1;
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

        /// <summary>The locate's route preference, per car: the road the route takes next from the last road the car
        /// was read on the route, HELD for 80 m after a node where it was read on another road (it is usually still on
        /// the ramp: the H2 race of 11-17 02:15 read the I-485 main line for its exit ramp, +19 km, until the ramp lay
        /// 10 m off the main line's centre).</summary>
        public struct PreferState
        {
            public int edge, from, field; public Vector3 devAt; public bool dev;
            public static PreferState None => new PreferState { edge = -1, from = -1, field = -1 };
        }

        public int Prefer(ref PreferState ps, Field F, int fieldId, in Loc loc, int dir, Vector3 pos)
        {
            int cur = loc.valid && !loc.offGraph ? loc.edge : -1;
            if (cur < 0) return ps.edge;
            if (ps.field != fieldId) { ps.field = fieldId; ps.edge = RouteNextEdge(F, loc, dir); ps.from = cur; ps.dev = false; return ps.edge; }
            if (cur != ps.from)
            {
                if (cur == ps.edge || ps.edge < 0) { ps.edge = RouteNextEdge(F, loc, dir); ps.dev = false; }
                else if (!ps.dev) { ps.dev = true; ps.devAt = pos; }
                ps.from = cur;
            }
            if (ps.dev && (pos - ps.devAt).sqrMagnitude > 80f * 80f) { ps.dev = false; ps.edge = RouteNextEdge(F, loc, dir); }
            return ps.edge;
        }

        /// <summary>The edge the field's route takes after the car's located edge (in the ruler's direction
        /// <paramref name="dir"/>), for <see cref="Locate"/>'s preference; -1 when none.</summary>
        public int RouteNextEdge(Field F, in Loc loc, int dir)
        {
            if (F == null || !loc.valid || loc.offGraph) return -1;
            var ed = map.edges[loc.edge];
            int st = loc.edge * 2 + (ed.oneway || dir != -1 ? 0 : 1);
            if (!legal[st]) return -1;
            int y = F.nxt[st];
            return y < 0 ? -1 : y >> 1;
        }

        /// <summary>A heading-aware distance: an edge across the car's travel scores up to 6 m worse (only while it
        /// moves at 3 m/s or more: a spinning car's heading means nothing).</summary>
        float HeadPen(int e, float s, Vector3 vel)
        {
            var v = new Vector2(vel.x, vel.z);
            float sp = v.magnitude;
            if (sp < 3f) return 0f;
            var t = map.edges[e].TangentAt(s);
            float cos = Vector2.Dot(v / sp, t);
            return 6f * (1f - Mathf.Abs(cos)) + (map.edges[e].oneway && cos < -0.3f ? OneWayAgainstM : 0f);
        }

        const float PreferBonusM = 3f, OneWayAgainstM = 8f;
        /// <summary>The suggested route's own roads (CheckpointRace sets it): a FRESH locate (the grid, a respawn) on a
        /// freeway with ramps, C-D roads and streets under it takes the route's road when the car is on its pavement
        /// (UptownLoop: a rival planned from the road beside the grid and spun 400 m off the course at 12 s).</summary>
        public HashSet<int> RouteSet;
        const float RouteSetBonusM = 6f;

        bool NearestScored(Vector3 pos, Vector3 vel, float r, float dy, int prefer, out int edge, out float s, out float dist, out float score)
            => NearestScored(pos, vel, r, dy, prefer, false, out edge, out s, out dist, out score);

        bool NearestScored(Vector3 pos, Vector3 vel, float r, float dy, int prefer, bool fresh, out int edge, out float s, out float dist, out float score)
        {
            edge = -1; s = 0f; dist = float.PositiveInfinity; score = float.PositiveInfinity;
            var p = new Vector2(pos.x, pos.z);
            var v = new Vector2(vel.x, vel.z);
            float sp = v.magnitude;
            if (sp >= 3f) v /= sp; else v = Vector2.zero;
            segs.Clear();
            map.EdgeSegsNear(p - new Vector2(r, r), p + new Vector2(r, r), segs);
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
                float sc = d;
                if (ei == prefer && d <= e.HalfMax + 1.5f) sc -= PreferBonusM;
                if (fresh && RouteSet != null && d <= e.HalfMax + 1.5f && RouteSet.Contains(ei)) sc -= RouteSetBonusM;
                if (v != Vector2.zero && L2 > 1e-8f)
                {
                    float cos = Vector2.Dot(v, ab / Mathf.Sqrt(L2));
                    sc += 6f * (1f - Mathf.Abs(cos));
                    // a ONE-WAY road the car is driving against: the other carriageway or a ramp the other way, 10 m
                    // off (CltH6: a ramp read for the parallel one coming back, +3.8 km for a second)
                    if (e.oneway && cos < -0.3f) sc += OneWayAgainstM;
                }
                if (sc < score) { score = sc; dist = d; edge = ei; s = sAt; }
            }
            return edge >= 0;
        }

        /// <summary>A FRESH placement on the graph (a planner with no road yet): heading-aware, the suggested route's
        /// roads first (<see cref="RouteSet"/>), within <paramref name="r"/> m and <paramref name="dy"/> m of height.</summary>
        public bool NearestFresh(Vector3 pos, Vector3 vel, float r, float dy, out int edge, out float s, out float dist)
            => NearestScored(pos, vel, r, dy, -1, true, out edge, out s, out dist, out _);

        /// <summary>The car's projection onto one edge: arc and plan distance, over the segments within
        /// <paramref name="window"/> metres of arc <paramref name="s0"/> (window &lt;= 0: every segment).</summary>
        public bool Project(int ei, Vector3 pos, float s0, float window, out float s, out float dist)
        {
            s = 0f; dist = float.PositiveInfinity;
            var e = map.edges[ei];
            if (e.pts == null || e.pts.Length < 2) return false;
            var p = new Vector2(pos.x, pos.z);
            for (int k = 0; k + 1 < e.pts.Length; k++)
            {
                if (window > 0f && (e.s[k + 1] < s0 - window || e.s[k] > s0 + window)) continue;
                Vector2 a = e.pts[k], b = e.pts[k + 1], ab = b - a;
                float L2 = ab.sqrMagnitude;
                float u = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / L2) : 0f;
                float d = Vector2.Distance(p, a + ab * u);
                if (d < dist) { dist = d; s = e.s[k] + u * (e.s[k + 1] - e.s[k]); }
            }
            return true;
        }

        bool NearSharedNode(int e1, int e2, Vector3 pos, float r)
        {
            var a = map.edges[e1]; var b = map.edges[e2];
            var p = new Vector2(pos.x, pos.z);
            float r2 = r * r;
            if ((a.a == b.a || a.a == b.b) && (map.nodes[a.a] - p).sqrMagnitude <= r2) return true;
            if ((a.b == b.a || a.b == b.b) && (map.nodes[a.b] - p).sqrMagnitude <= r2) return true;
            return false;
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
        public float Remaining(Field F, Vector3 pos, ref Loc loc, ref int dir)
        {
            if (loc.valid && !loc.offGraph)
            {
                // on (or kept by) an edge: the ruler at the car's projection, plus how far it is off the pavement
                var e = map.edges[loc.edge];
                // a two-way street: the way the car is going (dir, held by the caller; a fresh one from its travel)
                return RouteRemaining(F, loc.edge, loc.s, ref dir, float.PositiveInfinity, loc.dir) + Mathf.Max(0f, loc.dist - (e.HalfMax + 2f));
            }
            if (Nearest(pos, 60f, 8f, -1, 0f, out int ne, out float ns, out float nd))
            {
                int d0 = 0;
                float v = RouteRemaining(F, ne, ns, ref d0, 0f);
                dir = d0;
                return v + Mathf.Max(0f, nd - (map.edges[ne].HalfMax + 2f));
            }
            var p = new Vector2(pos.x, pos.z);
            float best = float.PositiveInfinity;
            segs.Clear();
            map.EdgeSegsNear(p - new Vector2(300f, 300f), p + new Vector2(300f, 300f), segs);
            foreach (int packed in segs)
            {
                var ed = map.edges[packed >> 12];
                float da = Vector2.Distance(p, map.nodes[ed.a]) + F.potM[ed.a];
                float db = Vector2.Distance(p, map.nodes[ed.b]) + F.potM[ed.b];
                if (da < best) best = da;
                if (db < best) best = db;
            }
            return best;
        }
    }
}
