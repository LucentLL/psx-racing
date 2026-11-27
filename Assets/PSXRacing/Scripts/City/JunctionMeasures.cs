using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// Road-surface stage 1: THE MEASURES SOURCE. The map has centre lines and lane counts, not kerbs; the junction
    /// outline solver (JunctionOutline) is the shape engine (tangent arcs, rounded noses, tapers) and this source hands
    /// it the NUMBERS. Three providers, one data shape:
    ///   RULES   (default; nothing loaded): the class table - LineModel.Extents for the borders, 15 / 12 / 9 / 6 m kerb
    ///           returns, noses where the map's links put them.
    ///   HAND    a text file named by PSX_CITY_MEASURES: numbers read off a reference by hand. It is a stand-in for
    ///           surveyed data and lives OUTSIDE the repository (nothing traced from a photo is checked in or baked).
    ///   SURVEY  (later) the same numbers computed from a surveyed pavement-polygon layer (HANDOFF.md, "THE MEASURES:
    ///           the spec for surveyed data", says how each number is computed and what a polygon cannot give).
    /// The numbers:
    ///   edge      per map edge, at stations (metres from the edge's a end): the pavement's offset LEFT and RIGHT of the
    ///             map line (in the edge's own direction). It replaces LineModel.Extents in the outline's arm borders
    ///             there, is held from the first station to the junction, and is blended back to the rule's width over
    ///             'blend' metres beyond the last station (where the ribbon, drawn by the rule, takes over).
    ///   return    per corner (edge A's left border, edge B's right border, anticlockwise): the kerb return's radius.
    ///   nose      per median end (edge A's left border, edge B's right border): two points of the nose's front (its cap
    ///             line), the shoulder radius, the kind (raised grass / raised concrete). A PAINTED median is no nose:
    ///             its edge measures carry the pavement across it and the two carriageways solve as one (abreast).
    ///   island    an island or channel strip the map lacks: a short closed polygon, a rounding radius per vertex, the
    ///             kind and the kerb height.
    ///   drop      a side arm the map has and the reference lacks.   join: two map nodes that are one junction.
    /// Plan coordinates are the map's (x east, z north, metres); the solver works about the cluster's origin.
    /// </summary>
    public static partial class JunctionMeasures
    {
        public sealed class Station { public float s, left = float.NaN, right = float.NaN; }
        public sealed class EdgeM
        {
            public int edge; public float blend = 12f; public List<Station> st = new List<Station>();
            /// <summary>The measure is HELD from its first station to the edge's a end / from its last to its b end (the
            /// end at a measured junction: the kerb runs on to it); the other way it blends back to the rule.</summary>
            public bool holdA, holdB;
            /// <summary>THE LANES' MEASURE (NaN: none): the lane bundle's sideways move off the rule's place (+ left of
            /// a -> b), held and blended as the offsets are. Without it the line model centres the rule's lanes in the
            /// measured pavement where BOTH sides are measured and leaves them where one side or none is. It is what a
            /// pavement edge cannot give: which lanes go on through a junction when the rule's lane count is off.</summary>
            public float shift = float.NaN;
        }
        public sealed class NoseM { public int a, b; public Vector2 capA, capB; public float r = 1.2f; public int kind; }
        public sealed class ReturnM { public int a, b; public float r; }
        public sealed class IslandM { public List<Vector2> pts = new List<Vector2>(); public List<float> r = new List<float>(); public int kind = 1; public float kerb = 0.15f; }
        public sealed class JunctionM
        {
            public string name = ""; public List<int> nodes = new List<int>();
            public List<NoseM> noses = new List<NoseM>(); public List<ReturnM> returns = new List<ReturnM>(); public List<IslandM> islands = new List<IslandM>();
            public List<int> drop = new List<int>(); public List<(int a, int b)> join = new List<(int, int)>();
            public Dictionary<int, float> rows = new Dictionary<int, float>();   // per edge: its end row at least this far out
        }
        public sealed class Set
        {
            public string provider = "hand";
            public Dictionary<int, EdgeM> edges = new Dictionary<int, EdgeM>();
            public List<JunctionM> junctions = new List<JunctionM>();
            /// <summary>The junction measure that names any of these map nodes (null: the rules).</summary>
            public JunctionM For(ICollection<int> clusterNodes)
            {
                foreach (var j in junctions) foreach (int n in j.nodes) if (clusterNodes.Contains(n)) return j;
                return null;
            }
        }

        static Set current; static bool loaded;
        /// <summary>The provider in force: null = RULES. HAND when PSX_CITY_MEASURES names a readable file.</summary>
        public static Set Current
        {
            get
            {
                if (loaded) return current;
                loaded = true; current = null;
                string path = System.Environment.GetEnvironmentVariable("PSX_CITY_MEASURES");
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    current = Parse(System.IO.File.ReadAllText(path));
                    Debug.Log($"[JunctionMeasures] provider {current.provider}: {current.edges.Count} edges, {current.junctions.Count} junctions from {path}");
                }
                return current;
            }
        }
        public static void Reset() { loaded = false; current = null; }

        /// <summary>A measures file's content as 8 hex digits (FNV-1a), "none" when it cannot be read: the tile bake's
        /// signature carries it (CityTileCache.Settings).</summary>
        public static string ContentTag(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return "none";
                uint h = 2166136261u;
                foreach (byte b in System.IO.File.ReadAllBytes(path)) { h ^= b; h *= 16777619u; }
                return h.ToString("x8");
            }
            catch (System.Exception) { return "none"; }
        }

        /// <summary>The text form (one item a line, '#' comments):
        ///   provider hand|survey
        ///   edge ID [blend M] [hold a|b|ab] [shift M]   then lines:  s STATION LEFT RIGHT   ('-' = no measure on that side)
        ///   junction NAME nodes N N ...  then lines:  nose A B X1 Z1 X2 Z2 r M kind grass|concrete
        ///                                             return A B r M
        ///                                             island kind concrete|grass kerb M pts X,Z,R X,Z,R ...
        ///                                             row EDGE M (the edge's end row at least M out)
        ///                                             drop EDGE      join N N</summary>
        public static Set Parse(string text)
        {
            var inv = CultureInfo.InvariantCulture;
            var set = new Set(); EdgeM e = null; JunctionM j = null;
            float F(string v) => float.Parse(v, inv);
            int Kind(string v) => v == "concrete" ? 1 : v == "painted" ? 2 : 0;
            foreach (var raw in text.Split('\n'))
            {
                string line = raw; int hash = line.IndexOf('#'); if (hash >= 0) line = line.Substring(0, hash);
                var t = line.Trim().Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (t.Length == 0) continue;
                switch (t[0])
                {
                    case "provider": set.provider = t[1]; break;
                    case "edge":
                        e = new EdgeM { edge = int.Parse(t[1], inv) }; j = null;
                        for (int i = 2; i + 1 < t.Length; i += 2) { if (t[i] == "blend") e.blend = F(t[i + 1]); else if (t[i] == "hold") { e.holdA = t[i + 1].Contains("a"); e.holdB = t[i + 1].Contains("b"); } else if (t[i] == "shift") e.shift = F(t[i + 1]); }
                        set.edges[e.edge] = e; break;
                    case "s":
                        if (e == null) break;
                        e.st.Add(new Station { s = F(t[1]), left = t[2] == "-" ? float.NaN : F(t[2]), right = t[3] == "-" ? float.NaN : F(t[3]) }); break;
                    case "junction":
                        j = new JunctionM { name = t[1] }; e = null;
                        for (int i = 3; i < t.Length; i++) j.nodes.Add(int.Parse(t[i], inv));
                        set.junctions.Add(j); break;
                    case "nose":
                        if (j == null) break;
                        { var ns = new NoseM { a = int.Parse(t[1], inv), b = int.Parse(t[2], inv), capA = new Vector2(F(t[3]), F(t[4])), capB = new Vector2(F(t[5]), F(t[6])) };
                          for (int i = 7; i + 1 < t.Length; i += 2) { if (t[i] == "r") ns.r = F(t[i + 1]); else if (t[i] == "kind") ns.kind = Kind(t[i + 1]); }
                          j.noses.Add(ns); }
                        break;
                    case "return":
                        if (j == null) break;
                        { var rt = new ReturnM { a = int.Parse(t[1], inv), b = int.Parse(t[2], inv) };
                          for (int i = 3; i + 1 < t.Length; i += 2) if (t[i] == "r") rt.r = F(t[i + 1]);
                          j.returns.Add(rt); }
                        break;
                    case "island":
                        if (j == null) break;
                        { var im = new IslandM(); int i = 1;
                          for (; i + 1 < t.Length && t[i] != "pts"; i += 2) { if (t[i] == "kind") im.kind = Kind(t[i + 1]); else if (t[i] == "kerb") im.kerb = F(t[i + 1]); }
                          for (i++; i < t.Length; i++) { var c = t[i].Split(','); im.pts.Add(new Vector2(F(c[0]), F(c[1]))); im.r.Add(c.Length > 2 ? F(c[2]) : 0.5f); }
                          if (im.pts.Count >= 3) j.islands.Add(im); }
                        break;
                    case "row": if (j != null) j.rows[int.Parse(t[1], inv)] = F(t[2]); break;
                    case "drop": if (j != null) j.drop.Add(int.Parse(t[1], inv)); break;
                    case "join": if (j != null) j.join.Add((int.Parse(t[1], inv), int.Parse(t[2], inv))); break;
                }
            }
            foreach (var em in set.edges.Values) em.st.Sort((p, q) => p.s.CompareTo(q.s));
            return set;
        }

        /// <summary>The measure of one side at an edge station: Catmull-Rom through the stations that carry that side
        /// (held beyond the ends). False when the side has no station.</summary>
        static bool Side(EdgeM m, bool left, float s, out float v, out float sMin, out float sMax)
        {
            v = 0f; sMin = sMax = 0f;
            var xs = new List<float>(); var ys = new List<float>();
            foreach (var st in m.st) { float y = left ? st.left : st.right; if (!float.IsNaN(y)) { xs.Add(st.s); ys.Add(y); } }
            int n = xs.Count; if (n == 0) return false;
            sMin = xs[0]; sMax = xs[n - 1];
            if (s <= xs[0]) { v = ys[0]; return true; }
            if (s >= xs[n - 1]) { v = ys[n - 1]; return true; }
            int i = 0; while (i + 2 < n && xs[i + 1] < s) i++;
            float x0 = xs[i], x1 = xs[i + 1], t = (s - x0) / Mathf.Max(x1 - x0, 1e-4f);
            // slopes: the neighbours' secant (zero at the ends: the measure is held beyond them)
            float m0 = i > 0 ? (ys[i + 1] - ys[i - 1]) / Mathf.Max(xs[i + 1] - xs[i - 1], 1e-4f) : 0f;
            float m1 = i + 2 < n ? (ys[i + 2] - ys[i]) / Mathf.Max(xs[i + 2] - xs[i], 1e-4f) : 0f;
            float h = x1 - x0, t2 = t * t, t3 = t2 * t;
            v = (2 * t3 - 3 * t2 + 1) * ys[i] + (t3 - 2 * t2 + t) * h * m0 + (-2 * t3 + 3 * t2) * ys[i + 1] + (t3 - t2) * h * m1;
            return true;
        }

        /// <summary>One side of an edge's measure at station s as the line model takes it: the offset and its weight
        /// (1 on the measured stations and toward a held end; easing to 0, the rule, over 'blend' beyond the others).</summary>
        public static bool SideAt(EdgeM m, bool left, float s, out float off, out float w, float edgeLength = float.MaxValue)
        {
            w = 0f;
            if (!Side(m, left, s, out off, out float sMin, out float sMax)) return false;
            // the blend ends half a metre before the edge does (the next edge is the rule's)
            if (s < sMin) w = m.holdA ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (sMin - s) / Mathf.Max(1f, Mathf.Min(m.blend, sMin - 0.5f)));
            else if (s > sMax) w = m.holdB ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (s - sMax) / Mathf.Max(1f, Mathf.Min(m.blend, edgeLength - 0.5f - sMax)));
            else w = 1f;
            return true;
        }

        /// <summary>An arm's borders from an edge measure. The arm is sampled every JunctionOutline.Step out from the
        /// junction; 'fwd' = it leaves from the edge's a end. The measure is held from its nearest station in to the
        /// junction and blended back to the border given (the rule's) over m.blend beyond its farthest station; the
        /// arm's measEnd is where that blend ends (its row lies at or beyond it).</summary>
        public static void ApplyToArm(JunctionOutline.Arm arm, bool fwd, float edgeLength, EdgeM m, bool rowsWait = true)
        {
            if (m == null || m.st.Count == 0) return;
            float step = JunctionOutline.Step; int n = arm.C.Count;
            float armLen = (n - 1) * step;
            for (int side = 0; side < 2; side++)
            {
                bool edgeLeft = side == 0;
                if (!Side(m, edgeLeft, 0f, out _, out float sMin, out float sMax)) continue;
                // the measured stretch in arm distance (out from the junction)
                float dFar = fwd ? sMax : edgeLength - sMin;
                dFar = Mathf.Clamp(dFar, 0f, armLen);
                float blend = Mathf.Max(1f, Mathf.Min(m.blend, armLen - step - dFar));
                bool armLeft = fwd ? edgeLeft : !edgeLeft;
                var border = armLeft ? arm.L : arm.R;
                for (int k = 0; k < n; k++)
                {
                    float d = k * step, s = fwd ? d : edgeLength - d;
                    Side(m, edgeLeft, s, out float off, out _, out _);
                    var t = (arm.C[Mathf.Min(k + 1, n - 1)] - arm.C[Mathf.Max(k - 1, 0)]).normalized;
                    var left = new Vector2(-t.y, t.x);
                    float sign = armLeft ? 1f : -1f;
                    float rule = Vector2.Dot(border[k] - arm.C[k], left) * sign;
                    float w = d <= dFar ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (d - dFar) / blend);
                    border[k] = arm.C[k] + left * (sign * Mathf.Lerp(rule, off, w));
                }
                if (armLeft) arm.measL = true; else arm.measR = true;
                // rowsWait: the ribbon beyond the row is the RULE's, so the row waits for the blend's end; false when the
                // line model carries the measure (JunctionMeasures.FeedLineModel): the ribbon has the measured edges too
                if (rowsWait) arm.measEnd = Mathf.Max(arm.measEnd, Mathf.Min(dFar + blend, armLen - step));
            }
        }

        /// <summary>A junction's measures for the solver, about the cluster's origin.</summary>
        public static JunctionOutline.Measures ToSolver(Set set, JunctionM j, Vector2 origin)
        {
            var m = new JunctionOutline.Measures { provider = set != null ? set.provider : "rules" };
            if (j == null) return m;
            foreach (var ns in j.noses) m.noses.Add(new JunctionOutline.Nose { a = ns.a, b = ns.b, capA = ns.capA - origin, capB = ns.capB - origin, r = ns.r, kind = ns.kind });
            foreach (var rt in j.returns) m.returns.Add(new JunctionOutline.Return { a = rt.a, b = rt.b, r = rt.r });
            foreach (var kv in j.rows) m.rows[kv.Key] = kv.Value;
            foreach (var im in j.islands)
            {
                var o = new JunctionOutline.Island { kind = im.kind, kerb = im.kerb };
                foreach (var p in im.pts) o.pts.Add(p - origin);
                o.r.AddRange(im.r);
                m.islands.Add(o);
            }
            return m;
        }
    }
}
