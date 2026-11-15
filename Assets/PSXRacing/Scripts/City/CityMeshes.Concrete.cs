using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// THE CONCRETE GATE (2026-11-15, the owner, looking at the deck where the
    /// ramps meet at I-85 and Freedom Drive: "can you see this massacre of
    /// unnecessary concrete median pieces? this is still an issue all across
    /// the city after weeks of attempts to correct them").
    ///
    /// Standing concrete - a Jersey, a parapet, a wall, a nose block, a V leg -
    /// is asked for by many rules (a carriageway's own median, union medians,
    /// ground twins, approach bands, gore noses, deck rails, retaining rails,
    /// cut walls, fan chords, split feet), each with its own start and stop,
    /// and every earlier round patched one rule and counted its class. Here
    /// EVERY piece, whatever rule asked for it, passes ONE function on its way
    /// into the barriers mesh (<see cref="ConGate"/>): its kind, its footprint
    /// in the world, its height, the rule and the road that asked. The gate
    /// keeps a record of what was asked and what it let through
    /// (<see cref="concreteLog"/>, for Editor/CityAudit.Concrete.cs: the audit
    /// reads the builder's own records, so the two cannot disagree), and it
    /// enforces what holds for all of them:
    ///   1. NOTHING ON PAVEMENT (PSX_CITY_CONC_PAVE=0 off): a piece with drawn
    ///      pavement - any road's ribbon, its own included, or a junction's
    ///      fan - on BOTH sides of it at the level it stands on is standing in
    ///      a driven surface. It is clipped to what stands outside; what is
    ///      left too short to be real goes. The one piece that belongs between
    ///      two pavements is a median barrier dividing OPPOSING traffic of a
    ///      divided road (not ramps): that stays.
    ///   2. HEIGHT BY KIND (PSX_CITY_CONC_SIZE=0 off): no piece stands taller
    ///      than its kind (a median barrier 0.81 m, a parapet 0.95 m).
    /// Decided from the map alone (the ribbons as drawn and the fans, whole
    /// map), so every tile draws the same. PSX_CITY_CONCRETE=0: the gate lets
    /// everything through as asked (the picture as it was).
    /// </summary>
    public static partial class CityMeshes
    {
        /// <summary>PSX_CITY_CONCRETE=0: every piece is built as asked (the A/B).</summary>
        public static bool ConcreteOn = System.Environment.GetEnvironmentVariable("PSX_CITY_CONCRETE") != "0";
        static readonly bool conPaveOff = System.Environment.GetEnvironmentVariable("PSX_CITY_CONC_PAVE") == "0";
        static readonly bool conSizeOff = System.Environment.GetEnvironmentVariable("PSX_CITY_CONC_SIZE") == "0";
        public static bool ConcretePaveOn => ConcreteOn && !conPaveOff;
        public static bool ConcreteSizeOn => ConcreteOn && !conSizeOff;

        public enum ConKind : byte { Jersey = 0, Rail = 1, UnionJersey = 2, UnionLeg = 3, InsetJersey = 4, RetainFace = 5, Headwall = 6, WBeam = 7, Unknown = 8 }

        /// <summary>One piece of standing concrete as a rule asked for it, or
        /// as the gate let it through. World space; a-b is the centre line of
        /// its footprint at the level it stands on.</summary>
        public struct ConcretePiece
        {
            public ConKind kind;
            /// <summary>The rule that asked (the caller's tag).</summary>
            public string rule;
            public int edge, side, node, tx, tz;
            public Vector3 a, b;
            /// <summary>Half its thickness in plan; its top over a-b at each end.</summary>
            public float half, hA, hB;
            public bool capA, capB, elev;
            /// <summary>0: asked, and changed by the gate (the BEFORE only); 1: a part the gate
            /// let through (the AFTER only); 2: asked and built as asked (both).</summary>
            public byte stage;
            /// <summary>Asked pieces: the metres of it standing on pavement (the gate's own test).</summary>
            public float paveM;
            /// <summary>Parts: this end was cut by the gate.</summary>
            public bool cutA, cutB;
        }

        /// <summary>Null in a build. An audit that sets it gets every piece
        /// the tiles it builds ask for and emit.</summary>
        public static List<ConcretePiece> concreteLog;
        /// <summary>The last tile build: barrier-mesh vertices no gate call accounts for
        /// (a writer the gate does not know: the audit fails on it).</summary>
        public static int concreteUnknownVerts;
        /// <summary>The last tile build: pieces asked, pieces changed, metres taken off pavement.</summary>
        public static int conAsked, conChanged; public static float conPaveCutM;

        // who is asking (set by the callers; a primitive has no other way to know)
        static string conRule = "?";
        static int conEdge = -1, conSide, conNode = -1;
        static bool conElev;
        static CityMap conMap; static Trims conTrims; static Vector3 conOrigin; static int conTx, conTz;
        static int conCovered;
        /// <summary>The gate looks at pieces at all (it is on, or an audit records).</summary>
        static bool conActive;
        /// <summary>The last piece through the gate was built as asked.</summary>
        static bool conLastWhole = true;

        public struct ConRun { public float t0, t1, k0, k1; public bool cut0, cut1; }
        /// <summary>The parts of the last piece the gate lets through (0..1 along it; k: its
        /// height there as a share of the asked height over TaperFootM).</summary>
        static readonly List<ConRun> conRuns = new List<ConRun>(6);

        const float ConPaveInsetM = 0.3f, ConStepM = 0.25f;
        /// <summary>The shortest part left standing after a clip: a parapet's (it may guard
        /// a fall beside it), anything else's.</summary>
        const float ConMinRailM = 0.5f, ConMinPartM = 3f;

        static void ConcreteTileBegin(CityMap map, Trims trims, TileMeshes tm, int tx, int tz)
        {
            conMap = map; conTrims = trims; conOrigin = tm.origin; conTx = tx; conTz = tz;
            conCovered = 0; conAsked = 0; conChanged = 0; conPaveCutM = 0f; concreteUnknownVerts = 0;
            conRule = "?"; conEdge = -1; conSide = 0; conNode = -1; conElev = false; conLastWhole = true;
            conActive = ConcreteOn || concreteLog != null;
            conEnclosed.Clear(); conPlateNow = false;
        }

        static void ConcreteTileEnd()
        {
            concreteUnknownVerts = barrierBucket.v.Count - conCovered;
            if (concreteLog != null && concreteUnknownVerts > 0 && barrierBucket.v.Count > 0)
            {
                var p = barrierBucket.v[barrierBucket.v.Count - 1] + conOrigin;
                concreteLog.Add(new ConcretePiece { kind = ConKind.Unknown, rule = "unknown-writer", edge = -1, node = -1, tx = conTx, tz = conTz, a = p, b = p, stage = 2, hA = concreteUnknownVerts });
            }
            conMap = null; conTrims = null;
        }

        /// <summary>Who asks for the pieces that follow.</summary>
        static void ConCtx(string rule, int edge, int side, int node, bool elev)
        {
            conRule = rule; conEdge = edge; conSide = side; conNode = node; conElev = elev;
        }

        /// <summary>The barrier bucket's vertices from <paramref name="from"/> on are a gated writer's.</summary>
        static void ConCover(int from) { conCovered += barrierBucket.v.Count - from; }

        /// <summary>A piece the gate records and does not judge (a retaining face under an
        /// edge, a culvert's headwall, a W-beam for the ends that tie into it).</summary>
        static void ConNote(ConKind kind, string rule, Vector3 a, Vector3 b, float half, float hA, float hB)
        {
            if (concreteLog == null) return;
            concreteLog.Add(new ConcretePiece
            {
                kind = kind, rule = rule, edge = conEdge, side = conSide, node = conNode, tx = conTx, tz = conTz,
                a = a + conOrigin, b = b + conOrigin, half = half, hA = hA, hB = hB, elev = conElev, stage = 2,
            });
        }

        /// <summary>A writer's vertices from <paramref name="from"/> on, recorded by their box
        /// (a headwall, a pipe's end: shapes with no line).</summary>
        static void ConNoteBox(ConKind kind, string rule, int from)
        {
            int n = barrierBucket.v.Count;
            conCovered += n - from;
            if (concreteLog == null || n <= from) return;
            Vector3 mn = barrierBucket.v[from], mx = mn;
            for (int i = from + 1; i < n; i++) { mn = Vector3.Min(mn, barrierBucket.v[i]); mx = Vector3.Max(mx, barrierBucket.v[i]); }
            var c = (mn + mx) * 0.5f; c.y = mx.y;
            concreteLog.Add(new ConcretePiece
            {
                kind = kind, rule = rule, edge = conEdge, side = conSide, node = conNode, tx = conTx, tz = conTz,
                a = c + conOrigin, b = c + conOrigin, half = 0.5f * Mathf.Max(mx.x - mn.x, mx.z - mn.z), hA = mx.y - mn.y, hB = mx.y - mn.y, stage = 2,
            });
        }

        /// <summary>Pavement the ribbons and fans do not hold: is the world plan point on it, and at what
        /// height? Null unless some builder lays such pavement (it must answer from the map alone, the
        /// same for every tile). The gate counts it as a junction's area.</summary>
        public delegate bool ExtraPave(Vector2 q, float y, out float yPave);
        public static ExtraPave ConcreteExtraPave;
        /// <summary><see cref="ConPaveAt"/>'s "who" for that pavement.</summary>
        const int ConPlate = -2000000000;

        // ---- the pavement under a piece -------------------------------------------
        static readonly HashSet<int> conSet = new HashSet<int>();
        static readonly HashSet<int> conEdgeSet = new HashSet<int>();
        static readonly HashSet<int> conFanSet = new HashSet<int>();
        static readonly List<int> conEdges = new List<int>(32);
        static readonly List<int> conFans = new List<int>(16);

        /// <summary>The roads and junction fans whose pavement could lie under a piece from a to b (world plan).</summary>
        static void ConGather(Vector2 a, Vector2 b, float extra = 0f)
        {
            conSet.Clear(); conEdgeSet.Clear(); conFanSet.Clear(); conEdges.Clear(); conFans.Clear();
            float r = 1.5f + CityElevation.MaxCorridorHalf + extra;
            conMap.EdgeSegsInRect(Vector2.Min(a, b) - Vector2.one * r, Vector2.Max(a, b) + Vector2.one * r, conSet);
            foreach (int packed in conSet)
            {
                int ei = packed >> 12;
                if (!conEdgeSet.Add(ei)) continue;
                var o = conMap.edges[ei];
                if (o.tunnel) continue;
                conEdges.Add(ei);
                for (int end = 0; end < 2; end++)
                {
                    int n = end == 0 ? o.a : o.b;
                    if (!conTrims.patch[n]) continue;
                    n = FanKey(conTrims, n);
                    if (!conFanSet.Add(n)) continue;
                    var fan = FanPolyOf(conMap, conTrims, n);
                    if (fan.tris != null && fan.tris.Length >= 3) conFans.Add(n);
                }
            }
        }

        /// <summary>Drawn pavement under the plan point q, at a height a piece standing at y
        /// stands in: whose (an edge's index; -1 - n for the fan of node n) and which way
        /// its traffic runs there (zero in a junction's area).</summary>
        static bool ConPaveAt(Vector2 q, float y, out int who, out Vector2 travel, out float yLane, bool loose = false, bool drawnOnly = false)
        {
            who = int.MinValue; travel = Vector2.zero; yLane = 0f;
            for (int k = 0; k < conEdges.Count; k++)
            {
                int ei = conEdges[k];
                var ol = OutlineOf(conMap, conTrims, ei);
                if (ol.L == null || q.x < ol.minX || q.x > ol.maxX || q.y < ol.minZ || q.y > ol.maxZ) continue;
                for (int i = 1; i < ol.L.Length; i++)
                {
                    if (ol.BlockMisses(i, q.x, q.y, q.x, q.y)) { i += Outline.Block - 1; continue; }
                    float yl;
                    if (TriInterval(ol.L[i - 1], ol.L[i], ol.R[i], q, Vector2.right, 0f, out _, out _)) yl = TriHeight(ol.L[i - 1], ol.L[i], ol.R[i], q);
                    else if (TriInterval(ol.L[i - 1], ol.R[i], ol.R[i - 1], q, Vector2.right, 0f, out _, out _)) yl = TriHeight(ol.L[i - 1], ol.R[i], ol.R[i - 1], q);
                    else continue;
                    if (loose ? Mathf.Abs(yl - y) > 3f : !RailStandsIn(y, yl)) continue;
                    yLane = yl;
                    who = ei;
                    Vector3 m0 = (ol.L[i - 1] + ol.R[i - 1]) * 0.5f, m1 = (ol.L[i] + ol.R[i]) * 0.5f;
                    var d = new Vector2(m1.x - m0.x, m1.z - m0.z);
                    d = d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.zero;
                    if (!conMap.edges[ei].oneway)
                    {
                        // a two-way ribbon: the half on the right of travel (L) runs with the line, the other against it
                        var mid = new Vector2((m0.x + m1.x) * 0.5f, (m0.z + m1.z) * 0.5f);
                        var toR = new Vector2(ol.R[i].x - ol.L[i].x, ol.R[i].z - ol.L[i].z);
                        if (Vector2.Dot(q - mid, toR) > 0f) d = -d;
                    }
                    travel = d;
                    return true;
                }
            }
            foreach (int nf in conFans)
            {
                var fan = fanPolys[nf];
                if ((q - fan.centre).sqrMagnitude > fan.reach * fan.reach) continue;
                if (OnFan(fan, q, out float yf) && (loose ? Mathf.Abs(yf - y) <= 3f : RailStandsIn(y, yf))) { who = -1 - nf; yLane = yf; return true; }
            }
            if (drawnOnly) return false;
            // pavement drawn by neither a ribbon nor a fan (a deck plate laid over the openings between
            // the ramps of an interchange on structure): whoever draws it answers here, from the map alone
            if (ConcreteExtraPave != null && ConcreteExtraPave(q, y, out float yp) && (loose ? Mathf.Abs(yp - y) <= 3f : RailStandsIn(y, yp))) { who = ConPlate; yLane = yp; return true; }
            // THE PLATE RULE (off by default): an opening the pavement of a structure closes round counts as its plate
            if (conPlateNow && !loose && ConInOpening(q, y)) { who = ConPlate; yLane = y; return true; }
            return false;
        }

        /// <summary>
        /// 3. THE PLATE (PSX_CITY_CONC_PLATE=1; default OFF). Where ramps meet ON a structure (the
        /// single-point interchanges) the deck is drawn as the ramps' own ribbons and the junction's
        /// fan, and the triangles left between them are OPEN (the road below shows through): every
        /// ribbon's edge there carries its parapet and every gore its nose, which is the "massacre"
        /// in the middle of the owner's deck - and each of them guards a real fall, so rule 1 leaves
        /// them. A real interchange deck is ONE plate: the parapet follows the outline of all the
        /// pavement at that level and nothing stands inside it. With this rule on, an opening that
        /// drawn pavement at the piece's level closes round on every side (twelve rays, each met
        /// within ConPlateReachM) counts as plate, and rule 1 then takes what stands at its rim. IT
        /// MUST ONLY BE ON ONCE THOSE OPENINGS ARE FLOORED (the deck plate is another round's work:
        /// branch charlotte-spui); until then it would open unguarded falls, hence the default.
        /// The parapet on the plate's OUTER outline has open air on one side and stays.
        /// </summary>
        public static bool ConcretePlateOn = System.Environment.GetEnvironmentVariable("PSX_CITY_CONC_PLATE") == "1";
        const float ConPlateReachM = 30f, ConPlateStepM = 1f;
        const int ConPlateRays = 12;
        static bool conPlateNow;
        static readonly Dictionary<long, bool> conEnclosed = new Dictionary<long, bool>();

        /// <summary>Is the plan point q (on no drawn pavement) in an opening drawn pavement at level y closes round?</summary>
        static bool ConInOpening(Vector2 q, float y)
        {
            long key = ((long)Mathf.RoundToInt(q.x * 2f) << 32) ^ (uint)Mathf.RoundToInt(q.y * 2f);
            if (conEnclosed.TryGetValue(key, out bool known)) return known;
            bool all = true;
            for (int k = 0; k < ConPlateRays && all; k++)
            {
                float ang = (k + 0.5f) * (2f * Mathf.PI / ConPlateRays);
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                bool met = false;
                for (float s = ConPlateStepM; s <= ConPlateReachM && !met; s += ConPlateStepM) met = ConPaveAt(q + dir * s, y, out _, out _, out _, false, true);
                all = met;
            }
            conEnclosed[key] = all;
            return all;
        }

        /// <summary>Does a piece's centre line at q (world plan; <paramref name="dir"/> along
        /// it, <paramref name="perp"/> across it, first toward the side away from its own
        /// road) stand ON pavement: drawn pavement past both its faces, and not the two
        /// carriageways of a divided road running opposite ways along it (R1: there a
        /// median barrier belongs)?</summary>
        static bool ConOnPavement(Vector2 q, Vector2 dir, Vector2 perp, float y, float half)
        {
            float off = half + ConPaveInsetM;
            if (!ConPaveAt(q + perp * off, y, out int w0, out var t0, out _)) return false;
            if (!ConPaveAt(q - perp * off, y, out int w1, out var t1, out _)) return false;
            if (w0 >= 0 && w1 >= 0 && !conMap.edges[w0].link && !conMap.edges[w1].link &&
                Vector2.Dot(t0, t1) < -0.5f && Mathf.Abs(Vector2.Dot(t0, dir)) > 0.7f) return false;
            return true;
        }

        static readonly List<bool> conOn = new List<bool>(64);

        /// <summary>
        /// THE GATE. A piece of <paramref name="kind"/> asked for from a to b (tile space:
        /// the centre line of its footprint, at the level it stands on),
        /// <paramref name="half"/> its half thickness, hA/hB its top over that line,
        /// <paramref name="outward"/> the side away from its own road (zero: none).
        /// Returns -1: build it as asked. Else the number of parts in
        /// <see cref="conRuns"/> to build instead (0: none of it).
        /// </summary>
        static int ConGate(ConKind kind, Vector3 a, Vector3 b, Vector2 outward, float half, float hA, float hB, bool capA, bool capB, bool whole = false)
        {
            conLastWhole = true;
            conAsked++;
            var a2 = new Vector2(a.x + conOrigin.x, a.z + conOrigin.z); var b2 = new Vector2(b.x + conOrigin.x, b.z + conOrigin.z);
            float len = Vector2.Distance(a2, b2);
            float paveM = 0f;
            int parts = -1;
            if (len >= 0.05f && conMap != null)
            {
                var dir = (b2 - a2) / len;
                var perp = new Vector2(dir.y, -dir.x);
                if (Vector2.Dot(perp, outward) < 0f) perp = -perp;
                conPlateNow = ConcretePlateOn && conElev;
                ConGather(a2, b2, conPlateNow ? ConPlateReachM : 0f);
                int n = Mathf.Max(2, Mathf.CeilToInt(len / ConStepM) + 1);
                conOn.Clear();
                int onCount = 0;
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / (n - 1);
                    bool on = ConOnPavement(Vector2.Lerp(a2, b2, t), dir, perp, Mathf.Lerp(a.y, b.y, t), half);
                    conOn.Add(on);
                    if (on) onCount++;
                }
                paveM = len * onCount / n;
                if (onCount > 0 && ConcretePaveOn && whole)
                {
                    // a piece that cannot be cut (a V's leg, a split's foot): none of it once half a metre stands on pavement
                    if (paveM >= 0.5f || onCount * 2 >= n) { conRuns.Clear(); parts = 0; conLastWhole = false; conChanged++; conPaveCutM += paveM; }
                }
                else if (onCount > 0 && ConcretePaveOn)
                {
                    // the parts that stand outside: from the first free sample to the last
                    conRuns.Clear();
                    float minPart = kind == ConKind.Rail ? ConMinRailM : ConMinPartM;
                    bool turnDown = kind != ConKind.Rail;   // a parapet keeps its height to its end (it guards a fall); a barrier's cut end is turned down
                    int from = -1;
                    for (int i = 0; i <= n; i++)
                    {
                        bool free = i < n && !conOn[i];
                        if (free) { if (from < 0) from = i; continue; }
                        if (from < 0) continue;
                        float t0 = (float)from / (n - 1), t1 = (float)(i - 1) / (n - 1);
                        bool cut0 = from > 0, cut1 = i < n;
                        float partM = (t1 - t0) * len;
                        from = -1;
                        if (partM < minPart) continue;
                        if (!turnDown || (!cut0 && !cut1)) { conRuns.Add(new ConRun { t0 = t0, t1 = t1, k0 = 1f, k1 = 1f, cut0 = cut0, cut1 = cut1 }); continue; }
                        // turned down to the foot height over TaperLenM from each cut end
                        float rampT = Mathf.Min(TaperLenM, (cut0 && cut1 ? 0.5f : 1f) * partM) / len;
                        float kTop = Mathf.Clamp01(rampT * len / TaperLenM);
                        float ta = t0, tb = t1;
                        if (cut0) { conRuns.Add(new ConRun { t0 = t0, t1 = t0 + rampT, k0 = 0f, k1 = kTop, cut0 = true }); ta = t0 + rampT; }
                        if (cut1) tb = t1 - rampT;
                        if (tb - ta > 1e-4f) conRuns.Add(new ConRun { t0 = ta, t1 = tb, k0 = kTop, k1 = kTop });
                        if (cut1) conRuns.Add(new ConRun { t0 = tb, t1 = t1, k0 = kTop, k1 = 0f, cut1 = true });
                    }
                    parts = conRuns.Count;
                    conLastWhole = false;
                    conChanged++;
                    conPaveCutM += paveM;
                }
            }
            if (concreteLog != null)
            {
                var rec = new ConcretePiece
                {
                    kind = kind, rule = conRule, edge = conEdge, side = conSide, node = conNode, tx = conTx, tz = conTz,
                    a = a + conOrigin, b = b + conOrigin, half = half, hA = hA, hB = hB, capA = capA, capB = capB, elev = conElev,
                    stage = (byte)(parts < 0 ? 2 : 0), paveM = paveM,
                };
                concreteLog.Add(rec);
                for (int k = 0; k < parts; k++)
                {
                    var r = conRuns[k];
                    var p = rec;
                    p.stage = 1; p.paveM = 0f;
                    p.a = Vector3.Lerp(rec.a, rec.b, r.t0); p.b = Vector3.Lerp(rec.a, rec.b, r.t1);
                    p.hA = ConH(Mathf.Lerp(hA, hB, r.t0), r.k0); p.hB = ConH(Mathf.Lerp(hA, hB, r.t1), r.k1);
                    p.capA = r.t0 <= 0f ? capA : r.cut0; p.capB = r.t1 >= 1f ? capB : r.cut1;
                    p.cutA = r.cut0; p.cutB = r.cut1;
                    concreteLog.Add(p);
                }
            }
            return parts;
        }

        /// <summary>A part's height: the asked height, turned down by k toward the foot height.</summary>
        static float ConH(float asked, float k) => k >= 1f ? asked : Mathf.Lerp(Mathf.Min(asked, TaperFootM), asked, k);

        /// <summary>The audit's probe, outside a tile build: the gate's own pavement test at one
        /// plan point (strict: a piece standing at y stands in it; loose: any within 3 m of y).</summary>
        public static bool ConcretePaveProbe(CityMap map, Trims trims, Vector2 q, float y, bool loose, out int who, out Vector2 travel, out float yLane)
        {
            conMap = map; conTrims = trims; conPlateNow = false;
            ConGather(q, q);
            return ConPaveAt(q, y, out who, out travel, out yLane, loose);
        }

        /// <summary>The audit's plan of a place: the pavement the gate tests against within r of c -
        /// every ribbon's cross-sections (O rows) and every fan's triangles (F rows), as text.</summary>
        public static void ConcretePlanDump(CityMap map, Trims trims, Vector2 c, float r, System.Text.StringBuilder sb)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            conMap = map; conTrims = trims;
            ConGather(c - Vector2.one * r, c + Vector2.one * r);
            foreach (int ei in conEdges)
            {
                var ol = OutlineOf(map, trims, ei);
                if (ol.L == null) continue;
                var e = map.edges[ei];
                for (int i = 0; i < ol.L.Length; i++)
                    sb.Append(string.Format(inv, "O\t{0}\t{1}\t{2:0.00}\t{3:0.00}\t{4:0.00}\t{5:0.00}\t{6:0.00}\t{7:0.00}\t{8}\t{9}\t{10}\t{11}\t{12}\n",
                        ei, i, ol.L[i].x, ol.L[i].y, ol.L[i].z, ol.R[i].x, ol.R[i].y, ol.R[i].z, ol.elev[i] ? 1 : 0, e.oneway ? 1 : 0, e.link ? 1 : 0, e.cls, e.name));
            }
            foreach (int nf in conFans)
            {
                var T = fanPolys[nf].tris;
                for (int i = 0; i + 2 < T.Length; i += 3)
                    sb.Append(string.Format(inv, "F\t{0}\t{1}\t{2:0.00}\t{3:0.00}\t{4:0.00}\t{5:0.00}\t{6:0.00}\t{7:0.00}\t{8:0.00}\t{9:0.00}\t{10:0.00}\n",
                        nf, i / 3, T[i].x, T[i].y, T[i].z, T[i + 1].x, T[i + 1].y, T[i + 1].z, T[i + 2].x, T[i + 2].y, T[i + 2].z));
            }
        }

        /// <summary>HEIGHT BY KIND: the most a piece of a kind stands over its line.</summary>
        static float ConCap(ConKind kind, float h)
        {
            if (!ConcreteSizeOn) return h;
            float cap = kind == ConKind.Rail ? RailH : BarrierH;
            return Mathf.Min(h, cap);
        }
    }
}
