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
    ///   3. THE PLATE (PSX_CITY_CONC_PLATE=1; OFF by default, see ConcretePlateOn):
    ///      an opening the pavement of a structure closes round counts as plate.
    ///   4. NO ORPHAN (PSX_CITY_CONC_ORPHAN=0 off): a run under 30 m that guards
    ///      nothing is not built (see ConcreteOrphanOn).
    ///   5. AN END IS A REAL END (PSX_CITY_CONC_ENDS=0 off): a barrier's free
    ///      square end is turned down (see ConcreteEndsOn).
    /// (There is no rule 2: the inventory found no piece taller or thicker
    /// than its kind - a barrier 0.81 m, a parapet 0.95 m - city-wide.)
    /// Rule 1 is decided from the map alone (the ribbons as drawn and the fans,
    /// whole map), so every tile draws the same; rules 4 and 5 look at the run
    /// as the tile holds it and leave alone any run that reaches within 10 m of
    /// the tile's border (the next tile may hold more of it). PROTECTION IS
    /// NEVER TAKEN: a piece on structure, with a fall beside it or holding land
    /// is clipped only where pavement lies past both its faces, and is never
    /// dropped or lowered. PSX_CITY_CONCRETE=0: the gate lets everything
    /// through as asked (the picture as it was).
    /// </summary>
    public static partial class CityMeshes
    {
        /// <summary>PSX_CITY_CONCRETE=0: every piece is built as asked (the A/B).</summary>
        public static bool ConcreteOn = System.Environment.GetEnvironmentVariable("PSX_CITY_CONCRETE") != "0";
        static readonly bool conPaveOff = System.Environment.GetEnvironmentVariable("PSX_CITY_CONC_PAVE") == "0";
        public static bool ConcretePaveOn => ConcreteOn && !conPaveOff;

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
            /// <summary>It stands on a retaining face (its caller says so): it guards an ungraded drop.</summary>
            public bool hold;
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
        /// <summary>The caller knows the pieces that follow stand on a retaining face (the run rules never take them).</summary>
        static bool conHold;
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
            ConBindPlates();
            conEnclosed.Clear(); conPlateNow = false;
            conTile.Clear(); conPend = 0; conOrphans = 0; conOrphanM = 0f;
        }

        /// <summary>Every piece of the tile is in the bucket (roads, junctions, medians, culverts):
        /// the run rules, before the ground is closed against what stands (CloseOpenGround reads the bucket).</summary>
        static void ConcreteRunRulesNow()
        {
            if (ConRunRulesOn) ConRunRules();
            conTile.Clear(); conPend = 0;
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
            conRule = rule; conEdge = edge; conSide = side; conNode = node; conElev = elev; conHold = false;
        }

        /// <summary>The barrier bucket's vertices from <paramref name="from"/> on are a gated writer's.</summary>
        static void ConCover(int from) { conCovered += barrierBucket.v.Count - from; }

        /// <summary>A piece the gate records and does not judge (a retaining face under an
        /// edge, a culvert's headwall, a W-beam for the ends that tie into it).</summary>
        static void ConNote(ConKind kind, string rule, Vector3 a, Vector3 b, float half, float hA, float hB)
        {
            // a W-beam or the cable: no concrete, but a run that ties into it is not an orphan
            if (kind == ConKind.WBeam && ConRunRulesOn && conActive) conTile.Add(new ConRec { kind = kind, a = a + conOrigin, b = b + conOrigin, hA = hA, hB = hB, log = -1, v0 = -1 });
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

        // ---- another round's deck plates, bound by name when they are in the build ------
        /// <summary>
        /// PSX_CITY_CONC_SPUI=1 (OFF by default; untested here: the plates are not in this branch).
        /// The deck plates of branch charlotte-spui (CityDeckPlates: one slab over the knot of
        /// ramps on a structure, a convex hull, one height) are pavement the ribbons and fans do
        /// not hold. With the switch on, the gate reads them by name (no compile-time tie) and
        /// counts a point as plate when it lies inside a plate's hull AND at least ConPlateRimM
        /// from its rim: rule 1 then takes every wall, chord rail and nose standing in the plate,
        /// and leaves the parapets within that distance of the rim, where the plate's edge is.
        /// Whoever turns it on runs the bridges audit after (RAIL-DROP must not rise): if the
        /// plate reaches further out than the old parapets stood, its rim needs a parapet of its own.
        /// </summary>
        public static bool ConcreteSpuiOn = System.Environment.GetEnvironmentVariable("PSX_CITY_CONC_SPUI") == "1";
        const float ConPlateRimM = 2f;
        static bool conPlatesTried, conPlatesWarned; static System.Type conPlatesType; static int conPlatesVersion = int.MinValue;
        static readonly List<(Vector2[] hull, Vector2 mn, Vector2 mx, float h)> conPlates = new List<(Vector2[], Vector2, Vector2, float)>();
        /// <summary>How many deck plates the gate reads (0: none bound).</summary>
        public static int ConcretePlatesBound => conPlates.Count;

        static void ConBindPlates()
        {
            if (!ConcreteSpuiOn) return;
            try
            {
                if (!conPlatesTried) { conPlatesTried = true; conPlatesType = typeof(CityMeshes).Assembly.GetType("PSXRacing.City.CityDeckPlates"); }
                if (conPlatesType == null) return;
                const System.Reflection.BindingFlags St = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
                const System.Reflection.BindingFlags In = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
                int v = conPlatesType.GetProperty("Version", St)?.GetValue(null) is int vi ? vi : 0;
                if (v == conPlatesVersion) return;
                conPlatesVersion = v;
                conPlates.Clear();
                bool on = conPlatesType.GetProperty("On", St)?.GetValue(null) is bool b1 && b1;
                bool cut = !(conPlatesType.GetField("CutOn", St)?.GetValue(null) is bool b2) || b2;
                if (on && cut && conPlatesType.GetField("Plates", St)?.GetValue(null) is System.Collections.IEnumerable list)
                    foreach (object plate in list)
                    {
                        var t = plate.GetType();
                        if (t.GetField("skipped", In)?.GetValue(plate) != null) continue;
                        if (t.GetField("cutFailed", In)?.GetValue(plate) is bool cf && cf) continue;
                        if (!(t.GetField("hull", In)?.GetValue(plate) is Vector2[] hull) || hull.Length < 3) continue;
                        if (!(t.GetField("H", In)?.GetValue(plate) is float h)) continue;
                        Vector2 mn = hull[0], mx = hull[0];
                        foreach (var q in hull) { mn = Vector2.Min(mn, q); mx = Vector2.Max(mx, q); }
                        conPlates.Add((hull, mn, mx, h));
                    }
                if (conPlates.Count > 0 && ConcreteExtraPave == null) ConcreteExtraPave = ConPlatePave;
            }
            catch (System.Exception ex)
            {
                conPlates.Clear();
                if (!conPlatesWarned) { conPlatesWarned = true; Debug.LogWarning("[Concrete] deck plates found but not bound: " + ex.Message); }
            }
        }

        /// <summary>Inside a bound plate's hull (convex, counter-clockwise), ConPlateRimM or more from its rim.</summary>
        static bool ConPlatePave(Vector2 q, float y, out float yPave)
        {
            yPave = 0f;
            foreach (var pl in conPlates)
            {
                if (q.x < pl.mn.x || q.x > pl.mx.x || q.y < pl.mn.y || q.y > pl.mx.y) continue;
                bool inside = true;
                for (int i = 0, j = pl.hull.Length - 1; i < pl.hull.Length && inside; j = i++)
                {
                    Vector2 a = pl.hull[j], e = pl.hull[i] - a;
                    float len = e.magnitude;
                    if (len > 1e-4f && (e.x * (q.y - a.y) - e.y * (q.x - a.x)) / len < ConPlateRimM) inside = false;
                }
                if (inside) { yPave = pl.h; return true; }
            }
            return false;
        }

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
                    a = a + conOrigin, b = b + conOrigin, half = half, hA = hA, hB = hB, capA = capA, capB = capB, elev = conElev, hold = conHold,
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
            if (ConRunRulesOn)
            {
                // what is built of it, for the run rules at the tile's end (ConRunRules)
                conPend = conTile.Count;
                int logAt = concreteLog != null ? concreteLog.Count - 1 - Mathf.Max(parts, 0) : -1;
                Vector3 wa = a + conOrigin, wb = b + conOrigin;
                bool args = conArgSet && kind == ConKind.Jersey;
                if (parts < 0)
                    conTile.Add(new ConRec
                    {
                        kind = kind, a = wa, b = wb, hA = hA, hB = hB, elev = conElev || conHold, log = logAt, v0 = -1,
                        args = args, ea = conArgA, eb = conArgB, outw = conArgOut, u0 = conArgV0, u1 = conArgV1, capA = capA, capB = capB,
                    });
                else
                    for (int k = 0; k < parts; k++)
                    {
                        var r = conRuns[k];
                        conTile.Add(new ConRec
                        {
                            kind = kind, a = Vector3.Lerp(wa, wb, r.t0), b = Vector3.Lerp(wa, wb, r.t1),
                            hA = ConH(Mathf.Lerp(hA, hB, r.t0), r.k0), hB = ConH(Mathf.Lerp(hA, hB, r.t1), r.k1), elev = conElev || conHold,
                            log = logAt < 0 ? -1 : logAt + 1 + k, v0 = -1,
                            args = args, ea = Vector3.Lerp(conArgA, conArgB, r.t0), eb = Vector3.Lerp(conArgA, conArgB, r.t1), outw = conArgOut,
                            u0 = Mathf.Lerp(conArgV0, conArgV1, r.t0), u1 = Mathf.Lerp(conArgV0, conArgV1, r.t1),
                            capA = r.t0 <= 0f ? capA : r.cut0, capB = r.t1 >= 1f ? capB : r.cut1,
                        });
                    }
            }
            conArgSet = false;
            return parts;
        }

        // ---- the run rules, at the tile's end -------------------------------------
        /// <summary>
        /// 4. NO ORPHAN (PSX_CITY_CONC_ORPHAN=0 off). A run - pieces joined end to end, whatever
        /// rules asked for them - shorter than ConOrphanM that guards nothing is not built at all.
        /// The 2026-10-03 rule (MinMedianRunM) dropped short runs of three rules, each counted
        /// along its own edge; this one counts the run as it stands, across rules and edges, after
        /// rule 1 has clipped it. A run stays if any piece of it is on structure, has a fall
        /// beside it or holds land (R2: protection is never taken), if it ties into a W-beam or
        /// the cable median, or if it comes within ConBorderM of the tile's border (it may carry
        /// on in the next tile, which this tile cannot see: such a run is left, by both tiles).
        /// </summary>
        static readonly bool conOrphanOff = System.Environment.GetEnvironmentVariable("PSX_CITY_CONC_ORPHAN") == "0";
        public static bool ConcreteOrphanOn => ConcreteOn && !conOrphanOff;
        /// <summary>
        /// 5. AN END IS A REAL END (PSX_CITY_CONC_ENDS=0 off). A barrier's free end - tied to no
        /// other piece, a W-beam or the cable - standing over ConEndSquareM is turned down to the
        /// foot height along its last piece (as the 2026-10-03 rule turns a median run's planned
        /// ends down: this takes the ends no plan knew of - a run cut by a gore gap, a skipped
        /// span, a node). Barriers only (EmitBarrier's pieces: a median Jersey, a cut wall, a
        /// carried median, a fan chord's Jersey) and only where the piece guards nothing: a
        /// parapet keeps its height to its end (it guards a fall), a wall holding land keeps it.
        /// </summary>
        static readonly bool conEndsOff = System.Environment.GetEnvironmentVariable("PSX_CITY_CONC_ENDS") == "0";
        public static bool ConcreteEndsOn => ConcreteOn && !conEndsOff;
        const float ConEndSquareM = 0.3f;
        /// <summary>The last tile build: free square barrier ends turned down.</summary>
        public static int conEndsTurned;
        static bool conArgSet; static Vector3 conArgA, conArgB; static Vector2 conArgOut; static float conArgV0, conArgV1;
        static byte[] conTied = new byte[0];
        static readonly List<(int i, float hA, float hB)> conRedo = new List<(int, float, float)>(8);
        static bool ConRunRulesOn => ConcreteOrphanOn || ConcreteEndsOn;

        /// <summary>Does another piece of the tile stand within 12 m ahead of an end, in its line?</summary>
        static bool ConBarrierAhead(int self, Vector3 end, Vector3 dir3)
        {
            var d = new Vector2(dir3.x, dir3.z);
            if (d.sqrMagnitude < 1e-6f) return false;
            d.Normalize();
            var e2 = new Vector2(end.x, end.z);
            for (float s = 1f; s <= 12f; s += 1f)
            {
                var q = e2 + d * s;
                if (!conGrid.TryGetValue(((long)Mathf.FloorToInt(q.x / 8f) << 32) ^ (uint)Mathf.FloorToInt(q.y / 8f), out var l)) continue;
                foreach (int j in l)
                {
                    if (j == self) continue;
                    var p = conTile[j];
                    Vector2 pa = new Vector2(p.a.x, p.a.z), pd = new Vector2(p.b.x - p.a.x, p.b.z - p.a.z);
                    float L2 = pd.sqrMagnitude;
                    float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(q - pa, pd) / L2) : 0f;
                    if (Vector2.Distance(q, pa + pd * t) <= ConTieM && Mathf.Abs(Mathf.Lerp(p.a.y, p.b.y, t) - end.y) <= 1.5f) return true;
                }
            }
            return false;
        }
        const float ConTieM = 0.75f, ConTieYM = 0.8f, ConOrphanM = MinMedianRunM, ConBorderM = 10f;
        /// <summary>The last tile build: pieces and metres the orphan rule left out.</summary>
        public static int conOrphans; public static float conOrphanM;

        struct ConRec
        {
            public ConKind kind; public Vector3 a, b; public float hA, hB; public bool elev, gone;
            /// <summary>Its vertices and triangle indices in the barrier bucket (v0 -1: none, a tie only), its entry in the log.</summary>
            public int v0, v1, t0, t1, log;
            /// <summary>A barrier's own arguments (EmitBarrierCore), should the end rule build it again.</summary>
            public bool args, capA, capB; public Vector3 ea, eb; public Vector2 outw; public float u0, u1;
        }
        static readonly List<ConRec> conTile = new List<ConRec>(512);
        static int conPend, conMv, conMt;
        static void ConMark() { conMv = barrierBucket.v.Count; conMt = barrierBucket.t.Count; }
        /// <summary>The piece (or part) the gate last let through is in the bucket from the mark on.</summary>
        static void ConEmitted()
        {
            if (conPend >= conTile.Count) return;
            var r = conTile[conPend];
            r.v0 = conMv; r.v1 = barrierBucket.v.Count; r.t0 = conMt; r.t1 = barrierBucket.t.Count;
            conTile[conPend++] = r;
        }

        static readonly Dictionary<long, List<int>> conGrid = new Dictionary<long, List<int>>();
        static readonly Stack<List<int>> conGridPool = new Stack<List<int>>();
        static int[] conPar = new int[0];
        static float[] conRunM = new float[0];
        static byte[] conRunFlag = new byte[0];   // 1: open (the tile's border, a W-beam, the cable), 2: guards
        static readonly float[] ConFallProbeM = { 1.5f, 3f, 5f, 8f, 12f };

        static int ConRoot(int i) { while (conPar[i] != i) { conPar[i] = conPar[conPar[i]]; i = conPar[i]; } return i; }

        /// <summary>A fall beside the piece (0.6 m within 8 m, 1.5 m within 12 m) or land it holds
        /// (land standing over it more steeply than a 1V:3H back slope), on the graded ground.</summary>
        static bool ConGuards(in ConRec r)
        {
            if (r.elev) return true;
            var a2 = new Vector2(r.a.x, r.a.z); var b2 = new Vector2(r.b.x, r.b.z);
            float len = Vector2.Distance(a2, b2);
            if (len < 0.05f) return false;
            var d = (b2 - a2) / len; var perp = new Vector2(d.y, -d.x); var m = (a2 + b2) * 0.5f;
            float y = 0.5f * (r.a.y + r.b.y);
            for (int sgn = -1; sgn <= 1; sgn += 2)
                foreach (float o in ConFallProbeM)
                {
                    float g = CityElevation.GroundY(conMap, m.x + perp.x * sgn * o, m.y + perp.y * sgn * o);
                    if (g < y - (o > 8f ? 1.5f : 0.6f) || g > y + Mathf.Max(0.3f, o / 3f)) return true;
                }
            return false;
        }

        static void ConRunRules()
        {
            conOrphans = 0; conOrphanM = 0f; conEndsTurned = 0;
            int n = conTile.Count;
            if (n == 0 || conMap == null) return;
            if (conPar.Length < n) { conPar = new int[n * 2]; conRunM = new float[n * 2]; conRunFlag = new byte[n * 2]; conTied = new byte[n * 2]; }
            foreach (var l in conGrid.Values) { l.Clear(); conGridPool.Push(l); }
            conGrid.Clear();
            const float Cell = 8f;
            float T = TileSize, x0 = conTx * T, z0 = conTz * T;
            for (int i = 0; i < n; i++)
            {
                conPar[i] = i; conRunM[i] = 0f; conRunFlag[i] = 0; conTied[i] = 0;
                var r = conTile[i];
                int cx0 = Mathf.FloorToInt((Mathf.Min(r.a.x, r.b.x) - ConTieM) / Cell), cx1 = Mathf.FloorToInt((Mathf.Max(r.a.x, r.b.x) + ConTieM) / Cell);
                int cz0 = Mathf.FloorToInt((Mathf.Min(r.a.z, r.b.z) - ConTieM) / Cell), cz1 = Mathf.FloorToInt((Mathf.Max(r.a.z, r.b.z) + ConTieM) / Cell);
                if ((cx1 - cx0 + 1) * (cz1 - cz0 + 1) > 64) { conRunFlag[i] = 1; continue; }   // (no piece is that long: left alone)
                for (int x = cx0; x <= cx1; x++)
                    for (int z = cz0; z <= cz1; z++)
                    {
                        long k = ((long)x << 32) ^ (uint)z;
                        if (!conGrid.TryGetValue(k, out var l)) conGrid[k] = l = conGridPool.Count > 0 ? conGridPool.Pop() : new List<int>(8);
                        l.Add(i);
                    }
            }
            // pieces joined end to end (or an end onto another's body) are one run
            for (int i = 0; i < n; i++)
            {
                var r = conTile[i];
                for (int end = 0; end < 2; end++)
                {
                    var e3 = end == 0 ? r.a : r.b;
                    // an end near the tile's border: its run may carry on where this tile cannot see
                    if (e3.x - x0 < ConBorderM || x0 + T - e3.x < ConBorderM || e3.z - z0 < ConBorderM || z0 + T - e3.z < ConBorderM) { conRunFlag[i] |= 1; conTied[i] |= (byte)(1 << end); }
                    if (!conGrid.TryGetValue(((long)Mathf.FloorToInt(e3.x / Cell) << 32) ^ (uint)Mathf.FloorToInt(e3.z / Cell), out var l)) continue;
                    var e2 = new Vector2(e3.x, e3.z);
                    foreach (int j in l)
                    {
                        if (j == i) continue;
                        var q = conTile[j];
                        Vector2 qa = new Vector2(q.a.x, q.a.z), qd = new Vector2(q.b.x - q.a.x, q.b.z - q.a.z);
                        float L2 = qd.sqrMagnitude;
                        float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(e2 - qa, qd) / L2) : 0f;
                        if (Vector2.Distance(e2, qa + qd * t) > ConTieM || Mathf.Abs(Mathf.Lerp(q.a.y, q.b.y, t) - e3.y) > ConTieYM) continue;
                        int ri = ConRoot(i), rj = ConRoot(j);
                        if (ri != rj) conPar[ri] = rj;
                        conTied[i] |= (byte)(1 << end);
                    }
                }
            }
            for (int i = 0; i < n; i++)
            {
                var r = conTile[i];
                int root = ConRoot(i);
                conRunM[root] += Vector2.Distance(new Vector2(r.a.x, r.a.z), new Vector2(r.b.x, r.b.z));
                if ((conRunFlag[i] & 1) != 0 || r.kind == ConKind.WBeam) conRunFlag[root] |= 1;
            }
            // a short run that is not open: does any piece of it guard?
            for (int i = 0; i < n; i++)
            {
                int root = ConRoot(i);
                if (conRunM[root] >= ConOrphanM || (conRunFlag[root] & 3) != 0) continue;
                if (ConGuards(conTile[i])) conRunFlag[root] |= 2;
            }
            bool any = false;
            conRedo.Clear();
            for (int i = 0; i < n; i++)
            {
                int root = ConRoot(i);
                var r = conTile[i];
                if (!ConcreteOrphanOn || conRunM[root] >= ConOrphanM || (conRunFlag[root] & 3) != 0 || r.v0 < 0 || r.v1 <= r.v0)
                {
                    // rule 5: a barrier's free square end, turned down along this piece
                    if (!ConcreteEndsOn || !r.args || r.v0 < 0 || r.v1 <= r.v0 || conTied[i] == 3) continue;
                    bool freeA = (conTied[i] & 1) == 0 && r.hA > ConEndSquareM, freeB = (conTied[i] & 2) == 0 && r.hB > ConEndSquareM;
                    // (not an end with more barrier ahead in its line: turned down, the two ends of a gap are a dip)
                    if (freeA && ConBarrierAhead(i, r.a, r.a - r.b)) freeA = false;
                    if (freeB && ConBarrierAhead(i, r.b, r.b - r.a)) freeB = false;
                    if ((!freeA && !freeB) || ConGuards(r)) continue;
                    conRedo.Add((i, freeA ? Mathf.Min(r.hA, TaperFootM) : r.hA, freeB ? Mathf.Min(r.hB, TaperFootM) : r.hB));
                    r.gone = true; conTile[i] = r; any = true;
                    continue;
                }
                r.gone = true; conTile[i] = r; any = true;
                conOrphans++; conOrphanM += Vector2.Distance(new Vector2(r.a.x, r.a.z), new Vector2(r.b.x, r.b.z));
                if (concreteLog != null && r.log >= 0)
                {
                    var e = concreteLog[r.log];
                    e.stage = (byte)(e.stage == 1 ? 3 : 0);
                    concreteLog[r.log] = e;
                }
            }
            if (any) ConRemoveGone();
            foreach (var (i, hA, hB) in conRedo)
            {
                var r = conTile[i];
                int v = barrierBucket.v.Count;
                EmitBarrierCore(r.ea, r.eb, r.outw, r.u0, r.u1, r.capA, r.capB, hA, hB);
                conCovered += barrierBucket.v.Count - v;
                conEndsTurned++;
                if (concreteLog != null && r.log >= 0)
                {
                    // the record: as asked (the before), and as built with its end turned down (the after)
                    var e = concreteLog[r.log];
                    if (e.stage == 2) { e.stage = 0; concreteLog[r.log] = e; e.stage = 1; e.hA = hA; e.hB = hB; concreteLog.Add(e); }
                    else { e.hA = hA; e.hB = hB; concreteLog[r.log] = e; }
                }
            }
            conChanged += conOrphans + conEndsTurned;
        }

        static readonly List<Vector3> conKeepV = new List<Vector3>(4096);
        static readonly List<Vector2> conKeepUv = new List<Vector2>(4096);
        static readonly List<int> conKeepT = new List<int>(8192);

        /// <summary>Takes the pieces marked gone out of the barrier bucket (their vertices and
        /// triangles; the pieces are in the order they were written, every piece's triangles
        /// index its own vertices alone).</summary>
        static void ConRemoveGone()
        {
            var bk = barrierBucket;
            conKeepV.Clear(); conKeepUv.Clear(); conKeepT.Clear();
            int v = 0, t = 0, shift = 0;
            foreach (var r in conTile)
            {
                if (!r.gone || r.v0 < v) continue;
                for (int i = v; i < r.v0; i++) { conKeepV.Add(bk.v[i]); conKeepUv.Add(bk.uv[i]); }
                for (int i = t; i < r.t0; i++) conKeepT.Add(bk.t[i] - shift);
                shift += r.v1 - r.v0;
                v = r.v1; t = r.t1;
            }
            for (int i = v; i < bk.v.Count; i++) { conKeepV.Add(bk.v[i]); conKeepUv.Add(bk.uv[i]); }
            for (int i = t; i < bk.t.Count; i++) conKeepT.Add(bk.t[i] - shift);
            bk.v.Clear(); bk.v.AddRange(conKeepV);
            bk.uv.Clear(); bk.uv.AddRange(conKeepUv);
            bk.t.Clear(); bk.t.AddRange(conKeepT);
            conCovered -= shift;
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
    }
}
