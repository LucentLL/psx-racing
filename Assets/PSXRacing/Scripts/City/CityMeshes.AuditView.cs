using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    public static partial class CityMeshes
    {
        /// <summary>Probe only (CityGroundEdges, PSX_GEDGE_FANS): a fan's ring
        /// as the tile builds lay it, corner by corner.</summary>
        public static List<string> DebugFanRing(CityMap map, Trims trims, int node)
        {
            var o = new List<string>();
            if (!trims.patch[node]) { o.Add($"node {node}: no fan"); return o; }
            int fk = FanKey(trims, node);
            var ring = new List<FanCorner>(32);
            FanCorners(map, trims, fk, Vector3.zero, ring);
            o.Add($"node {node}: fan key {fk}, {ring.Count} corners");
            foreach (var k in ring)
                o.Add($"  ({k.pos.x:0.00},{k.pos.z:0.00}) y {k.pos.y:0.00} e{k.edge} side {k.side} node {k.node}{(k.mouthNext ? " MOUTH->" : "")}{(k.extra ? " extra" : "")}{(k.arc ? " arc" : "")}");
            ClearSectionCaches(); fanPolys.Clear(); fanStructure.Clear();
            return o;
        }

        // ---- what the audit records ask the tile builds to keep -------------
        // Null in a build: nothing is recorded and nothing changes. Set only
        // between AuditView.BeginRecord and EndRecord.
        static List<AuditView.SpanRecord> spanLog;
        static List<AuditView.PierRecord> pierLog;
        static List<AuditView.GoreRecord> goreLog;
        static List<AuditView.SpanRecord> spansKept;
        static List<AuditView.PierRecord> piersKept;
        static List<AuditView.GoreRecord> goresKept;

        /// <summary>
        /// THE AUDITS' WINDOW ONTO THE BUILDER (2026-10-02, plan P0, critic C4).
        ///
        /// The Editor audits compile into their own assembly, so they see only
        /// what is <c>public</c> here, and the roads pass's audits (TWIN, PAINT,
        /// MERGE, COVERAGE ...) need the builder's own answers: the lines a
        /// ribbon draws, the clip tables, the cross-sections, what stands on
        /// each side of each span, the piers, the gores. This class is how they
        /// read them: READ-ONLY (nothing here changes what a tile draws) and
        /// FROZEN (plan rule 4: no member's signature changes once it is on
        /// main; anything new is an overload or a new member).
        ///
        /// TWO KINDS OF ANSWER:
        ///  - The LAST TILE BUILD's tables (<see cref="SectionsOf"/>,
        ///    <see cref="ClipAt"/>, <see cref="ClipsOf"/>,
        ///    <see cref="SectionAt"/>, which reads the clips): a tile build
        ///    clears and refills them, so ask right after
        ///    <see cref="CityMeshes.Build"/> (or a TileJob's Finish) of the tile
        ///    that owns the place asked about, and before the next build.
        ///  - RECORDS, kept across builds between <see cref="BeginRecord"/> and
        ///    <see cref="EndRecord"/>: every span a tile DRAWS with its side flags
        ///    (each span once city-wide: a span belongs to the tile holding its
        ///    midpoint), every pier it stands (likewise once), every branch
        ///    end's gore P and N (once per tile within reach of the branch end:
        ///    de-duplicate by node and branch edge). Rails are
        ///    <see cref="railLog"/>, set the same way.
        ///
        /// SIDES. Side -1 is a section's L vertex, which is the RIGHT of travel
        /// along the edge (a to b); side +1 its R vertex, the LEFT of travel
        /// (on a one-way carriageway, the median side). <see cref="SectionView.right"/>
        /// points from L to R. World space throughout (metres; x east, z north).
        /// </summary>
        /// <summary>
        /// NO ISOLATED BARRIER PIECES (hotfix 2026-10-03, the owner's "stray
        /// concrete median blocks between roads where they shouldn't exist"):
        /// a median Jersey, a cut wall or a union's standing median shorter
        /// than this, ending inside its road at both ends, is not drawn.
        /// PSX_CITY_KEEP_SHORT=1 keeps them (the audit's before-count).
        /// </summary>
        public const float MinMedianRunM = 30f;
        public static readonly bool KeepShortBarriers = System.Environment.GetEnvironmentVariable("PSX_CITY_KEEP_SHORT") == "1";

        /// <summary>Does a union run stand its median up - a Jersey (Barrier)
        /// or mountable curbs (Raised)? Not a flush strip, and not on a run
        /// shorter than <see cref="MinMedianRunM"/> closed at both ends.</summary>
        public static bool UnionDrawsSolid(UnionRun r) =>
            r.median != DeckPairs.Median.Flush && r.median != DeckPairs.Median.None &&
            (KeepShortBarriers || !(r.open0 && r.open1 && r.s1 - r.s0 < MinMedianRunM));

        public static class AuditView
        {
            // ================================================================
            //  Cross-sections
            // ================================================================

            /// <summary>One cross-section of a ribbon, in world space.</summary>
            public struct SectionView
            {
                public float s;
                /// <summary>The drawn edge vertices: L on the right of travel, R on the left.</summary>
                public Vector3 L, R;
                /// <summary>The OSM line's point (world plan), and the unit
                /// direction from L to R (the left of travel).</summary>
                public Vector2 P, right;
                /// <summary>The ribbon's centre off the OSM line along right,
                /// and the half width it was cut from (taper and mitre applied).</summary>
                public float centre, hw;
                public bool elev;
                /// <summary>The inner edge was moved onto the host (a clipped
                /// branch), or the whole ribbon is inside the host (zero width).</summary>
                public bool clippedIn, collapsed;
                /// <summary>Which vertex is the clipped inner one: -1 L, +1 R, 0 none.</summary>
                public int innerSide;
                /// <summary>The squeeze moved that edge in (a parallel neighbour).</summary>
                public bool squeezedL, squeezedR;
                /// <summary>The neighbour squeezed against on that side (edge
                /// index, or -1), the arc on it, and the strip left between the
                /// two drawn edges (-1: none).</summary>
                public int nbL, nbR;
                public float nbAtL, nbAtR, stripL, stripR;

                /// <summary>Lateral of the L / R vertex off the OSM line along
                /// <see cref="right"/> (L is normally negative).</summary>
                public float LatL => Vector2.Dot(new Vector2(L.x, L.z) - P, right);
                public float LatR => Vector2.Dot(new Vector2(R.x, R.z) - P, right);
                public Vector3 Edge(int side) => side < 0 ? L : R;
                public int Nb(int side) => side < 0 ? nbL : nbR;
            }

            /// <summary>
            /// A fresh cross-section of <paramref name="e"/> at arc
            /// <paramref name="s"/>, exactly as the builder cuts one: the line
            /// model's extents, the clip (from the last tile build's clip table)
            /// and, when asked, the squeeze. What a whole ribbon does to its
            /// sections afterwards (the eased squeeze, buried ends, a grounded
            /// section stepping onto a neighbour's pavement) is not in it:
            /// <see cref="SectionsOf"/> has the sections as drawn.
            /// </summary>
            public static SectionView SectionAt(CityMap map, Trims trims, CityMap.Edge e, float s, bool squeeze = true) =>
                ViewOf(CityMeshes.SectionAt(map, trims, e, s, squeeze));

            /// <summary>
            /// The sections the LAST TILE BUILD cut <paramref name="edge"/>
            /// into, in arc order, world space: after the eased squeeze and the
            /// buried-end join, before a grounded section's height steps onto a
            /// neighbour's pavement (MeetPavement, heights only). The tile's own
            /// edges and the neighbours it looked at; 0 when that build did not
            /// section the edge.
            /// </summary>
            public static int SectionsOf(int edge, List<SectionView> into)
            {
                into.Clear();
                if (!rawSectionCache.TryGetValue(edge, out var list)) return 0;
                foreach (var c in list) into.Add(ViewOf(c));
                return into.Count;
            }

            static SectionView ViewOf(in Section c) => new SectionView
            {
                s = c.s, L = c.L, R = c.R, P = c.P, right = c.right, centre = c.centre, hw = c.hw,
                elev = c.elev, clippedIn = c.clippedIn, collapsed = c.collapsed, innerSide = c.innerSide,
                squeezedL = c.sqL, squeezedR = c.sqR,
                nbL = c.nbL, nbR = c.nbR, nbAtL = c.nbAtL, nbAtR = c.nbAtR, stripL = c.stripL, stripR = c.stripR,
            };

            // ================================================================
            //  Paint
            // ================================================================

            /// <summary>
            /// The painted lines the ribbon draws at a section: the line model's
            /// (<see cref="LineModel.LinesAt"/>), the edge line on a squeezed
            /// side moved in with the edge, none on a clipped inner side, and
            /// nothing past an edge line or within 2 cm of a drawn edge - the
            /// builder's own rule, called as the builder calls it. Each line's
            /// kind is <c>LineModel.LayoutOf(e).kind[line.k]</c> (the edge's own lines, L2)
            /// (<see cref="LineModel.KEdgeP"/> ...), its lateral off the OSM line
            /// along <see cref="SectionView.right"/>. (A span whose two sections
            /// are both full width is drawn as one plain quad of the profile's
            /// texture: the same lines at the same places.)
            /// </summary>
            public static void DrawnLines(CityMap.Edge e, in SectionView c, List<LineModel.LineAt> into)
            {
                var lay = LineModel.LayoutOf(e);
                LineModel.Extents(e, c.s, out float eM, out float eP);
                var sec = new Section { s = c.s, clippedIn = c.clippedIn, innerSide = c.innerSide, collapsed = c.collapsed };
                CityMeshes.DrawnLines(e, lay, sec, eM, eP, c.LatL, c.LatR, into);
            }

            /// <summary><see cref="DrawnLines(CityMap.Edge, in SectionView, List{LineModel.LineAt})"/>
            /// at a fresh section (<see cref="SectionAt"/>, squeezed).</summary>
            public static void DrawnLinesAt(CityMap map, Trims trims, CityMap.Edge e, float s, List<LineModel.LineAt> into)
            {
                var c = SectionAt(map, trims, e, s, true);
                DrawnLines(e, c, into);
            }

            // ================================================================
            //  Clips (a branch ribbon cut against its host)
            // ================================================================

            /// <summary>One clipped arc range of a branch edge.</summary>
            public struct ClipView
            {
                /// <summary>The branch edge, and its clipped arc range (it may
                /// run a metre past either end of the edge).</summary>
                public int edge;
                public float sFrom, sTo;
                /// <summary>Which side of the host the branch lies on, in the
                /// host CHAIN's frame (+1 = the chain's left).</summary>
                public int side;
                /// <summary>Which of the branch's own vertices faces the host
                /// (+1 = its R vertex).</summary>
                public int innerSide;
                /// <summary>The host chain: its first edge and its length.</summary>
                public int hostFirst;
                public float hostLength;
            }

            static ClipView ViewOf(int edge, Clip c) => new ClipView
            {
                edge = edge, sFrom = c.sFrom, sTo = c.sTo, side = c.side, innerSide = c.innerSide,
                hostFirst = c.host != null && c.host.edges.Count > 0 ? c.host.edges[0].index : -1,
                hostLength = c.host != null ? c.host.Length : 0f,
            };

            /// <summary>The clip over arc <paramref name="s"/> of a branch edge
            /// (last tile build), or false.</summary>
            public static bool ClipAt(CityMap.Edge e, float s, out ClipView clip)
            {
                var c = CityMeshes.ClipAt(e, s);
                clip = c != null ? ViewOf(e.index, c) : default;
                return c != null;
            }

            /// <summary>Every clipped range of one branch edge (last tile build).</summary>
            public static int ClipsOf(int edge, List<ClipView> into)
            {
                into.Clear();
                if (clips.TryGetValue(edge, out var list)) foreach (var c in list) into.Add(ViewOf(edge, c));
                return into.Count;
            }

            /// <summary>Every branch edge the last tile build clipped.</summary>
            public static int ClippedEdges(List<int> into)
            {
                into.Clear();
                foreach (var kv in clips) if (kv.Value.Count > 0) into.Add(kv.Key);
                into.Sort();
                return into.Count;
            }

            /// <summary>The host chain's edges, in chain order, of the clip over
            /// arc <paramref name="s"/> of a branch edge; 0 when unclipped there.</summary>
            public static int HostChainOf(CityMap.Edge e, float s, List<int> into)
            {
                into.Clear();
                var c = CityMeshes.ClipAt(e, s);
                if (c?.host == null) return 0;
                foreach (var h in c.host.edges) into.Add(h.index);
                return into.Count;
            }

            /// <summary>The host edge under arc <paramref name="s"/> of a
            /// clipped branch edge, or -1 (<see cref="CityMeshes.HostEdgeAt"/>).</summary>
            public static int HostEdgeAt(CityMap.Edge e, float s) => CityMeshes.HostEdgeAt(e, s);

            /// <summary>Are two edges a host and a branch of one clip (last tile
            /// build)? They overlap by design and are never squeezed apart.</summary>
            public static bool ClipPair(int a, int b) => clipPairs.Contains(PairKey(a, b));

            // ================================================================
            //  Records: spans and their sides, piers, gores
            // ================================================================

            /// <summary>What stands on one side of a drawn span.</summary>
            public struct SideView
            {
                /// <summary>Another surface owns this edge: a gore, a clipped
                /// branch's inner side, a zero-width wedge.</summary>
                public bool gap;
                /// <summary>A rail (deck parapet, approach or warranted-drop rail),
                /// a retaining face under it, a retaining wall in a cut, a Jersey
                /// median barrier.</summary>
                public bool rail, retain, cut, median;
                /// <summary>Why <see cref="cut"/>: 1 the back slope misses the
                /// land, 2 a road above stands in it, 3 a building does; 0 a run
                /// closed over a gap.</summary>
                public byte cutWhy;
                /// <summary>This span starts / ends its side's run of one kind.</summary>
                public bool capStart, capEnd;
                /// <summary>Plan A2: a twin-deck union's inner side (one
                /// structure with the road beside it; the owner draws the median).</summary>
                public bool union;
                /// <summary>Anything that stands up on this side.</summary>
                public bool Walled => rail || cut || median;
            }

            /// <summary>One span a tile drew (between two of its sections).</summary>
            public struct SpanRecord
            {
                public int edge;
                public float s0, s1;
                /// <summary>On structure; one end collapsed (a wedge); within an
                /// approach run of a structure end; inside the tile's flag
                /// window (where the ground was probed).</summary>
                public bool elev, wedge, approach, decided;
                /// <summary>Side -1 (L, right of travel) and +1 (R, left).</summary>
                public SideView l, r;
                public SideView Side(int side) => side < 0 ? l : r;
            }

            /// <summary>One pier a tile stood under a deck.</summary>
            public struct PierRecord
            {
                public int edge;
                /// <summary>Where it stands, and where it was first asked for
                /// (a pier is nudged along the deck off the roads below).</summary>
                public float s, sAsked;
                /// <summary>The column's foot (0.6 m into the ground) and top
                /// (the deck's underside), world space.</summary>
                public Vector3 foot, top;
                /// <summary>Half its width across the deck; the deck's direction.</summary>
                public float halfWidth;
                public Vector2 along;
            }

            /// <summary>
            /// A branch end beside its host (a ramp at its merge or diverge, the
            /// minor arm of a fork): the gore's two ends as the builder found
            /// them. P is where the branch's outer edge leaves the host's drawn
            /// edge, so the branch first has a surface of its own beside the
            /// host; N is the last sample still attached to the host, where the
            /// painted gore ends and the nose stands.
            /// </summary>
            public struct GoreRecord
            {
                public int node;
                /// <summary>The branch's edge at the node, and the host's.</summary>
                public int branchEdge, hostEdge;
                /// <summary>Host-chain frame: +1 = the chain's left; 0 = never
                /// clearly off the host's centreline (nothing is clipped).</summary>
                public int side;
                /// <summary>At least one sample was attached to the host.</summary>
                public bool attached;
                /// <summary>P exists (the branch opened beside the host).</summary>
                public bool opened;
                /// <summary>P: its host-chain arc (-1 when not opened), and the
                /// host's drawn edge there (world, the host's height).</summary>
                public float pArc;
                public Vector3 p;
                /// <summary>N: host-chain and branch-chain arcs (-1 when never
                /// attached), the host's edge and the branch's inner edge there
                /// (world, each road's height).</summary>
                public float nArc, nTravelled;
                public Vector3 nOuter, nInner;
                /// <summary>The branch parted from its host at N (false: the
                /// walk ran out of reach while still attached).</summary>
                public bool detached;
                /// <summary>A nose stands at N; the host is on structure there.</summary>
                public bool nosed, elevated;
                /// <summary>Times the branch folded back INTO its host after
                /// opening (a re-entry).</summary>
                public int reclosures;
            }

            // ================================================================
            //  Twin-deck unions (plan A2): from the trims, not a tile build
            // ================================================================

            /// <summary>One union run on one side of one edge (plan A2,
            /// <see cref="UnionRun"/>): the partner lies on <see cref="side"/>
            /// (+1 the left of travel) over arcs s0..s1; the partner's own run
            /// is nb0..nb1 on <see cref="nb"/>.</summary>
            public struct UnionRunView
            {
                public int edge, nb, side, pair;
                public float s0, s1, nb0, nb1;
                public DeckPairs.Median median;
                /// <summary>This edge draws the median; the run lies over an
                /// approach (on the ground); nothing carries it on past s0 / s1.</summary>
                public bool owner, approach, open0, open1;
                /// <summary>The median is drawn standing up (a Jersey or
                /// mountable curbs): not a flush strip, and not a run too short
                /// to stand one (<see cref="UnionDrawsSolid"/>).</summary>
                public bool solid;
            }

            /// <summary>Every union run of an edge (both sides), in side then
            /// arc order; 0 when it has none.</summary>
            public static int UnionRunsOf(Trims trims, int edge, List<UnionRunView> into)
            {
                into.Clear();
                var list = trims?.unions != null && edge >= 0 && edge < trims.unions.Length ? trims.unions[edge] : null;
                if (list == null) return 0;
                foreach (var r in list)
                    into.Add(new UnionRunView
                    {
                        edge = r.edge, nb = r.nb, side = r.side, pair = r.pair, s0 = r.s0, s1 = r.s1,
                        nb0 = r.mirror.s0, nb1 = r.mirror.s1, median = r.median,
                        owner = r.owner, approach = r.approach, open0 = r.open0, open1 = r.open1,
                        solid = UnionDrawsSolid(r),
                    });
                return into.Count;
            }

            static readonly List<SpanRecord> noSpans = new List<SpanRecord>();
            static readonly List<PierRecord> noPiers = new List<PierRecord>();
            static readonly List<GoreRecord> noGores = new List<GoreRecord>();

            /// <summary>Start recording: every tile built from now on adds its
            /// spans, piers and gores. Clears the previous recording.</summary>
            public static void BeginRecord()
            {
                spansKept = spanLog = new List<SpanRecord>(4096);
                piersKept = pierLog = new List<PierRecord>(256);
                goresKept = goreLog = new List<GoreRecord>(256);
            }

            /// <summary>Stop recording; what was recorded stays readable until
            /// the next <see cref="BeginRecord"/>.</summary>
            public static void EndRecord() { spanLog = null; pierLog = null; goreLog = null; }

            public static bool Recording => spanLog != null;
            public static IReadOnlyList<SpanRecord> Spans => (IReadOnlyList<SpanRecord>)spansKept ?? noSpans;
            public static IReadOnlyList<PierRecord> Piers => (IReadOnlyList<PierRecord>)piersKept ?? noPiers;
            public static IReadOnlyList<GoreRecord> Gores => (IReadOnlyList<GoreRecord>)goresKept ?? noGores;
        }

        // ---- the builder's side of the records (called only while recording) ----

        static void RecordSpan(CityMap.Edge e, in Section A, in Section B, in SpanFlags f)
        {
            AuditView.SideView Side(in SideFlags sf) => new AuditView.SideView
            {
                gap = sf.gap, rail = sf.rail, retain = sf.retain, cut = sf.cut, median = sf.median,
                cutWhy = sf.cutWhy, capStart = sf.capStart, capEnd = sf.capEnd, union = sf.union,
            };
            spanLog.Add(new AuditView.SpanRecord
            {
                edge = e.index, s0 = A.s, s1 = B.s,
                elev = f.elev, wedge = f.wedge, approach = f.approach, decided = f.decided,
                l = Side(f.l), r = Side(f.r),
            });
        }

        static void RecordPier(CityMap.Edge e, float sAsked, float s, Vector2 p, float footY, float topY, float hw, Vector2 tan)
        {
            pierLog.Add(new AuditView.PierRecord
            {
                edge = e.index, s = s, sAsked = sAsked,
                foot = new Vector3(p.x, footY, p.y), top = new Vector3(p.x, topY, p.y),
                halfWidth = hw, along = tan,
            });
        }

        static void RecordGore(CityMap map, Trims trims, int node, CityMap.Edge L, CityMap.Edge M, Chain host, int side,
                               float attachedTo, bool lastOk, bool detached, in BranchSample last, bool open, float openFrom)
        {
            var g = new AuditView.GoreRecord
            {
                node = node, branchEdge = L.index, hostEdge = M.index, side = side,
                attached = attachedTo >= 0f, pArc = -1f, nArc = -1f, nTravelled = -1f,
                detached = detached, nosed = lastOk && detached,
            };
            float pAt = -1f;
            foreach (var (a0, _, reclosed) in hostOpen)
            {
                if (pAt < 0f) pAt = a0;
                if (reclosed) g.reclosures++;
            }
            if (pAt < 0f && open) pAt = openFrom;
            if (pAt >= 0f && side != 0 && host.Walk(Mathf.Clamp(pAt, 0f, host.Length), out var Hp, out float sHp, out var pHp, out var dHp))
            {
                var rHp = new Vector2(-dHp.y, dHp.x);
                var ep = pHp + rHp * (side * HostHalf(map, trims, Hp, sHp, rHp, side));
                g.opened = true; g.pArc = pAt; g.p = new Vector3(ep.x, Hp.YAt(sHp), ep.y);
            }
            if (g.attached && last.H != null)
            {
                g.nArc = last.hostArc; g.nTravelled = attachedTo;
                g.nOuter = new Vector3(last.outer.x, last.yOut, last.outer.y);
                g.nInner = new Vector3(last.inner.x, last.yIn, last.inner.y);
                g.elevated = last.H.ElevatedAt(last.sM);
            }
            goreLog.Add(g);
        }
    }
}
