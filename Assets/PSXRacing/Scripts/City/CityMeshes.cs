using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// Turns one 256 m tile of the CityMap into meshes. Pure geometry — no
    /// GameObjects; CityWorld wraps the results. Everything is deterministic
    /// and TILE-LOCAL: vertices are relative to the tile's origin corner, so
    /// float precision never depends on how far from uptown the tile sits.
    ///
    /// Ownership rule at tile seams: a road span, river span, junction or
    /// building belongs to the tile that contains its MIDPOINT (or node, or
    /// footprint centre), so nothing is ever emitted twice. Adjacent tiles
    /// derive shared boundary vertices from the same stations and the same
    /// GroundY, so edges meet exactly.
    ///
    /// WINDING, ONCE. Three helpers decide which way every face points so no
    /// call site has to: <see cref="Bucket.Up"/> orders a horizontal quad so
    /// it faces UP (its corners read counter-clockwise in map view),
    /// <see cref="Bucket.Down"/> the reverse, and <see cref="Bucket.Wall"/>
    /// orders a vertical quad so it faces the OUTWARD normal it is given. The
    /// inside-out buildings and the roads visible only from underneath were
    /// both per-call-site winding errors; so were the kerbs, the deck
    /// fascias and the soffits (2026-09-12: every one of them faced INTO the
    /// road, so a bridge seen from beside or below was a floating ribbon
    /// with rails). The road ribbon keeps its verified order.
    ///
    /// JUNCTIONS (2026-09-12). A node with three or more arms used to trim
    /// every arm back by the widest road's half width plus two metres and
    /// fill the hole with a FLAT fan at the node's height. Two things were
    /// wrong with that. The fan was flat while the arms are not: a ramp
    /// leaving a junction at 8% has dropped nearly a metre by the time its
    /// ribbon starts, so every junction on a grade had a step at every
    /// mouth — the "ledges" at every bridge approach. And on a freeway a
    /// merge node is not a junction at all: OSM joins the ramp to the
    /// carriageway at the END of the taper, so the ramp's last hundred
    /// metres run INSIDE the mainline ribbon, through the mainline's
    /// barrier. Now: fan corners take each arm's own height at its own trim;
    /// trims are per ARM and only as long as the arms that actually overlap
    /// need; and a node whose arms are a through pair plus shallow BRANCHES
    /// (a merge, a diverge, a freeway fork) draws no fan at all — the
    /// through road runs on unbroken and each branch's ribbon is CLIPPED
    /// against it, starting as a zero-width wedge at the mainline's edge and
    /// widening as it leaves, with the mainline's barrier and rail standing
    /// down only where the branch has a surface of its own beside it.
    ///
    /// EDGES (2026-09-13). What a car meets past a drawn edge follows
    /// RoadsideRules: a grounded edge meets the ground through an exact VERGE
    /// in the ground mesh (an inch down, a shoulder, 1V:4H, its toe tucked
    /// under the lattice) with a render-only face over the inch; a barrier
    /// stands only where one is warranted — structure, the approach past it,
    /// a drop that grading could not remove, a freeway median, a retaining
    /// wall in a cut. See EmitSide. Where two roads run closer than a verge's
    /// run the ground between is a CONNECTOR from one pavement edge to the
    /// other (SolveStrip), and every side that stands down for another
    /// surface lays a flush SEAM strip under the join, because the two are
    /// never drawn from the same vertices.
    /// </summary>
    public static partial class CityMeshes
    {
        public const float TileSize = 256f;
        public const int GroundRes = 32;          // 8 m cells
        /// <summary>Metres of road per texture repeat — and therefore the
        /// length of ONE DASH CYCLE, because the painter draws the broken lane
        /// line as the first quarter of the repeat. 12.192 m is 40 feet, the
        /// US standard: a 10 ft stripe and a 30 ft gap.</summary>
        public const float RoadVTile = 12.192f;
        public const float RailH = 0.95f;
        /// <summary>How far a rail's traffic face stands INSIDE the drawn
        /// edge. The drive audit's cross-lane ray starts 0.6 m in, so the
        /// face stays at 0.3.</summary>
        public const float RailW = 0.3f;
        /// <summary>How far the rail's solid carries on OUTSIDE the drawn
        /// edge, so its collider is RailW + this = 0.6 m deep (the stage
        /// walls are solid boxes; the city's rails were 0.3 m three-face
        /// shells on the ROAD layer, so a wheel ray could land on the top and
        /// nothing called them a wall). The overhang also floors the 0.3 m
        /// strip between two squeezed decks.</summary>
        public const float RailOverhangM = 0.3f;

        /// <summary>One rail a tile emitted, for the probes: whose (an
        /// edge's side, or a fan chord at a node), where (world space, the
        /// drawn edge points it was laid along), and which way the traffic is
        /// (<see cref="inA"/>/<see cref="inB"/>, unit, plan).</summary>
        public struct RailRecord
        {
            public int edge, side, node;
            public float s0, s1;
            public Vector3 a, b;
            public Vector2 inA, inB;
            /// <summary>How far its solid reaches past the edge it was laid along.</summary>
            public float overhang;
        }
        /// <summary>Null in a build. A probe that sets it gets every rail the
        /// tiles it builds emit (<see cref="RailRecord"/>), so a solid met in
        /// a lane can be named by the rail that stands there.</summary>
        public static List<RailRecord> railLog;
        /// <summary>Null in a build. A probe that sets it gets every ground
        /// triangle the tiles lay off the lattice — verges, seams, half
        /// strips, shelves, fan chord verges and corner fills — in world
        /// space, with what laid it (<see cref="groundTag"/>), so land met
        /// over a lane can be named by the strip that stands there.</summary>
        public static List<(string tag, Vector3 a, Vector3 b, Vector3 c)> groundLog;
        static string groundTag = "";

        public const float PierEvery = 26f;
        public const float BuildingSink = 0.55f;
        /// <summary>A Jersey barrier: 81 cm tall, half a metre thick, on the
        /// paved edge of every freeway carriageway that is on the ground
        /// (decks carry rails). It is what keeps a race on the freeway and
        /// what a freeway looks like.</summary>
        public const float BarrierH = 0.81f;
        public const float BarrierW = 0.5f;
        /// <summary>How far the painted face under a grounded edge reaches:
        /// RENDER-ONLY. It hides the inch between the tarmac and the verge
        /// (RoadsideRules.EdgeDropM) and the lattice under it. It was 0.32 m
        /// deep and part of the roads collider — a vertical 20 cm wall the
        /// length of every street, which the car's body box met before its
        /// wheel rays crossed the edge.</summary>
        public const float KerbFaceM = 0.10f;

        // ---- the verge: how a grounded road meets the ground ---------------
        /// <summary>Shoulder width of a street's verge before its foreslope
        /// (synthesis 7.1: hw -> hw + 0.6 at -0.025 -> -0.049).</summary>
        public const float VergeShoulderM = 0.6f;
        /// <summary>A freeway or expressway carriageway's OUTSIDE shoulder:
        /// wider, as a real one is.</summary>
        public const float FreewayShoulderM = 1.2f;
        /// <summary>How far along an edge one verge cross-section is solved
        /// against the lattice. The lattice's slope breaks at 8 m cell edges;
        /// a verge toe straight across 10 m of them could float up to a tenth
        /// of a metre over it, and the toe tuck is exactly that deep.</summary>
        const float VergeStepM = 2.5f;
        /// <summary>The widest a verge ever runs looking for the ground, at
        /// 1V:4H past its shoulder. A drop it cannot catch in
        /// this is a warranted barrier, not a longer ramp. 12 m since WP-14:
        /// a fill now falls from the bench at 1V:4H (RoadsideRules
        /// .CityFillSlope) and the lattice's chord across the bench's edge
        /// lies under it, so the verge meets it up to a cell past the bench
        /// rather than on an 11.5 m flat.</summary>
        const float VergeMaxRunM = 12f;
        /// <summary>Resolution of the verge's search for the lattice.</summary>
        const float VergeSearchStepM = 0.25f;
        /// <summary>Plan clearance a verge keeps from another road's pavement
        /// as drawn: it never lies over someone else's lanes.</summary>
        const float VergeClearPadM = 0.2f;
        /// <summary>How far above a strip another road's pavement stands
        /// before ClearRun calls it a deck the ground passes under.</summary>
        const float DeckOverheadM = 0.5f;
        /// <summary>How far under the next road's pavement a CONNECTOR verge
        /// (see SolveStrip) ends, so its toe tucks out of sight beneath that
        /// road's edge instead of standing at it.</summary>
        const float ConnectorTuckInM = 0.05f;
        /// <summary>How far a ray must run on a pavement from its first point
        /// before ClearRun and FanEntry call it STARTED on that pavement: a
        /// strip starting on a drawn edge or corner, or a hair off one, only
        /// grazes it.</summary>
        const float VertexSlackM = 0.05f;
        /// <summary>The steepest connector laid between two close roads that
        /// a recoverable one cannot join (1V:1.5H): past it the verge tucks
        /// straight down, and a barrier is left to the drop warrant.</summary>
        const float SteepConnectorSlope = 1f / 1.5f;

        // ---- warranted rails ---------------------------------------------------
        /// <summary>How far a deck's rails carry on past each structure end
        /// onto the grounded approach: "at least two spans (20 m)". The land
        /// beside the first metres of an approach is a lattice cell that
        /// still leans down to the dug soffit.</summary>
        public const float ApproachRailM = 20f;
        /// <summary>Metres of a warranted run's grounded end that flare away
        /// from the road at RoadsideRules.EndFlareRatio: the stages' three
        /// four-metre stations, 0.8 m out at the tip.</summary>
        const float FlareLenM = RoadsideRules.EndFlareStations * 4f;
        /// <summary>
        /// THE SLOPED END (hotfix 2026-10-03, the owner's I-277 race: "90
        /// degree concrete formations ... they ended my race"). A barrier end
        /// that faces traffic - a median Jersey's, a union median's on the
        /// ground, a gore nose's V legs - is turned down: its top falls from
        /// full height to <see cref="TaperFootM"/> (a curb a wheel rides over)
        /// over <see cref="TaperLenM"/> (1:10 for a Jersey, the sloped concrete
        /// end of the Roadside Design Guide), so a car meets a ramp and never a
        /// square face. A rail stays full height (it guards a drop); one
        /// running out into NOTHING still flares away from the road
        /// (<see cref="FlareLenM"/>). Leftover item 2 (2026-10-03): a rail's
        /// grounded run end is sloped too - by carrying the concrete on
        /// <see cref="TaperLenM"/> past it where the roadside has the room, so
        /// it keeps its full height where it stood (see the rail-end pass in
        /// DecideSideFlags, and the W-beam lead-in below).
        /// </summary>
        public const float TaperLenM = 8f, TaperFootM = 0.12f;
        /// <summary>
        /// THE W-BEAM LEAD-IN (leftover item 2, 2026-10-03; the owner's W 5th
        /// frame: a parapet starting on its approach as a blunt concrete block
        /// with a flat grey end). Where the open roadside past a rail's grounded
        /// run end holds <see cref="WBeamLenM"/>, a W-beam guardrail on wood
        /// posts (the furniture atlas's pack metal and wood) leads into the
        /// parapet: bolted along its sloped end for <see cref="TaperLenM"/> (the
        /// transition), then its <see cref="WBeamTermM"/> terminal (37.5 ft)
        /// flared <see cref="WBeamFlareM"/> (4 ft) away from the road and turned
        /// down into the ground over its last <see cref="WBeamDownM"/>. Its top
        /// <see cref="WBeamTopM"/> (31 in) over the road edge, the beam
        /// <see cref="WBeamDepthM"/> (12.25 in) deep, a post every
        /// <see cref="WBeamPostM"/> (6 ft 3 in). Drawn by CityPoles with the
        /// tile's furniture (no new draw); solid as the tile's "Guardrail"
        /// collider (<see cref="TileMeshes.guardrails"/>).
        /// </summary>
        public const float WBeamTermM = 11.43f, WBeamFlareM = 1.22f, WBeamDownM = 2.5f,
                           WBeamTopM = 0.79f, WBeamDepthM = 0.31f, WBeamPostM = 1.905f;
        public const float WBeamLenM = TaperLenM + WBeamTermM;
        /// <summary>The beam's front this far inside the concrete's traffic
        /// face (RailW inside the edge), so the two never share a plane.</summary>
        const float WBeamInsetM = 0.05f;
        /// <summary>How far past a rail's end the open roadside is walked: far
        /// enough to see the next barrier's end before two lead-ins could
        /// meet (each takes half of a shared stretch).</summary>
        const float RailEndWalkM = 2f * WBeamLenM + 1f;
        /// <summary>What a lead-in or a sloped end keeps clear of a node or
        /// whatever else ends the open roadside.</summary>
        const float RailEndClearM = 1f;
        /// <summary>The W-beam drawn in pieces at most this long (its flare
        /// and turned-down tip follow them).</summary>
        const float WBeamStepM = 2.5f;
        /// <summary>The painted gore's width a sloped nose's apex needs (the two
        /// legs' feet meeting there).</summary>
        const float NoseApexRoomM = 0.6f;
        /// <summary>How far below a grounded rail's surface its outer face is
        /// buried (plus half its flare), so the verge never shows under it.</summary>
        const float RailBuryM = 0.35f;
        /// <summary>How far a warranted barrier's flags are decided beyond
        /// the tile, so a run end just outside it is seen by the span inside.
        /// 60 m since leftover item 2: a rail end's plan walks up to
        /// <see cref="RailEndWalkM"/> past it, and its W-beam reaches
        /// <see cref="WBeamLenM"/> - every tile that draws a piece of it must
        /// see the same open roadside.</summary>
        const float FlagReachM = 60f;
        /// <summary>Retaining wall runs: a run ends only after this many
        /// consecutive spans out of the cut, and a run shorter than
        /// <see cref="CutRunMinSpans"/> is not built. Decided per span it
        /// flickered on one DEM sample against a 2 m threshold and stopped at
        /// every ramp, and each gap was a door to the shelf behind.</summary>
        const int CutRunEndSpans = 3;
        const int CutRunMinSpans = 4;
        /// <summary>The widest the solid ground behind a retaining wall runs
        /// back at the wall's top before it meets the hill.</summary>
        const float CutShelfMaxM = 24f;

        /// <summary>Longest edge of one facade panel, in metres. Affine warp is
        /// proportional to how much a triangle spans, so a 40 m wall as one
        /// quad swims; PS1 content subdivided, and so does this.</summary>
        const float FacadePanelMax = 9f;
        const int FacadePanelCap = 4;
        /// <summary>Up a TOWER the cap is doubled: four panels over a 200 m
        /// wall are 50 m each, and the curtain wall visibly bowed in every
        /// preview. Only walls past this height pay for it.</summary>
        const float TallWallM = 40f;
        const int TallPanelCap = 8;

        /// <summary>What a stretch of road is MADE of, which is a separate
        /// question from what is painted on it: the same carriageway is fresh
        /// blacktop on the ground and poured concrete where it crosses a
        /// river. Ported from RG2's (material x age) pair.</summary>
        public enum Surface { AsphaltNew = 0, AsphaltOld, ConcreteNew, ConcreteOld }
        public const int SurfaceCount = 4;

        /// <summary>Driving surfaces are one row per RoadProfiles entry plus
        /// the unpainted junction slab, times four surfaces.</summary>
        public const int JunctionProfile = RoadProfiles.ProfileCount;
        public const int RoadClassCount = RoadProfiles.ProfileCount + 1;

        public enum Slot
        {
            Ground = 0, Concrete, Water, Pavement,
            FacadeTower, FacadeMid, FacadeBrick, Shops,
            FacadeGlass, FacadeHouse, RoofTiles, RoofFlat,
            // Driving surfaces, laid out as RoadFirst + profile * SurfaceCount +
            // surface. Arithmetic rather than eighty named members.
            RoadFirst,
            COUNT = RoadFirst + RoadClassCount * SurfaceCount,
        }

        public static Slot SlotOf(int profile, Surface surf) =>
            (Slot)((int)Slot.RoadFirst + profile * SurfaceCount + (int)surf);

        /// <summary>
        /// Has this stretch been resurfaced recently? RG2's own _roadAge hash,
        /// ported constant for constant. 40% new, deterministic from position
        /// alone — so a road is the same age on every visit, across a tile
        /// unload, with nothing to store.
        /// </summary>
        public static bool IsFresh(Vector2 seed)
        {
            unchecked
            {
                int x = (int)(seed.x * 100f), y = (int)(seed.y * 100f);
                uint h = (uint)((x * unchecked((int)0x9e3779b1)) ^ (y * unchecked((int)0x6a09e667)));
                h ^= h >> 16;
                h *= 0x85ebca6b;
                h ^= h >> 13;
                h *= 0xc2b2ae35;
                h ^= h >> 16;
                return h % 100u < 40u;
            }
        }

        /// <summary>Concrete where the road is on structure, asphalt where it
        /// is on the ground. A bridge deck IS poured concrete.</summary>
        public static Surface SurfaceOf(CityMap.Edge e, bool elevated)
        {
            bool fresh = IsFresh(e.hasAgeSeed ? e.ageSeed : e.pts[0]);   // one age per chain (WP-11)
            return elevated ? (fresh ? Surface.ConcreteNew : Surface.ConcreteOld)
                            : (fresh ? Surface.AsphaltNew : Surface.AsphaltOld);
        }

        public static Slot RoadSlot(CityMap.Edge e, bool elevated) =>
            SlotOf(e.profile, SurfaceOf(e, elevated));

        /// <summary>A freeway carriageway on the ground carries a barrier
        /// on its median side (and on the outside only in a cut — see the
        /// span loop). Expressway carriageways too; ramps and two-way roads
        /// do not (a ramp's edge is where its gore is).</summary>
        public static bool Barriered(CityMap.Edge e) =>
            !e.link && ((e.cls >= 5) || (e.cls == 4 && e.oneway));

        // ------------------------------------------------------------------
        //  THE FACADE ATLAS (Uptown B1, 2026-10-04)
        // ------------------------------------------------------------------
        /// <summary>One column of <c>Art/City/Facade/city_facade_atlas.png</c>
        /// (tools/city/facade_atlas.py holds the same table): how many floors
        /// one repeat of it shows, how many metres of wall one repeat covers
        /// at the office floor, and the column's mean colour in LINEAR light
        /// (what an OSM building:colour is divided by to make the tint).</summary>
        struct FacadeLook
        {
            public float floors, uM;
            public Vector3 meanLin;
            public FacadeLook(float floors, float uM, float r, float g, float b)
            { this.floors = floors; this.uM = uM; meanLin = new Vector3(r, g, b); }
        }
        const int LookSilver = 0, LookBlue = 1, LookTeal = 2, LookStone = 3, LookBrick = 4;
        static readonly FacadeLook[] FacadeLooks =
        {
            new FacadeLook(16f, 30.4f, 0.248f, 0.269f, 0.270f),   // silver grid curtain wall: 14 bays of 2.2 m
            new FacadeLook(20f, 38.0f, 0.066f, 0.081f, 0.094f),   // blue glass: 16 bays of 2.4 m
            new FacadeLook(24f, 45.6f, 0.065f, 0.091f, 0.092f),   // teal glass: 16 bays of 2.85 m
            new FacadeLook(4f, 14.1f, 0.272f, 0.242f, 0.205f),    // precast stone, punched windows
            new FacadeLook(4f, 12.4f, 0.215f, 0.172f, 0.153f),    // brick, punched windows (aspect true at 3.1 m)
        };
        /// <summary>Real storey heights: an office floor, a home's.</summary>
        const float OfficeFloorM = 3.8f, HomeFloorM = 3.1f;
        /// <summary>Linear multipliers per look, picked by hash where OSM tags
        /// no colour: a street of towers in three glasses and five shades,
        /// never one blue-grey (the before-shots' first fault).</summary>
        static readonly Vector3[][] LookPalette =
        {
            new[] { new Vector3(1.00f, 1.00f, 1.00f), new Vector3(0.92f, 0.98f, 1.10f), new Vector3(1.08f, 1.05f, 0.98f),
                    new Vector3(0.80f, 0.84f, 0.90f), new Vector3(1.15f, 1.15f, 1.15f) },
            new[] { new Vector3(1.30f, 1.40f, 1.60f), new Vector3(1.10f, 1.25f, 1.55f), new Vector3(1.50f, 1.55f, 1.70f),
                    new Vector3(0.95f, 1.05f, 1.30f), new Vector3(1.35f, 1.35f, 1.40f) },
            new[] { new Vector3(1.25f, 1.45f, 1.40f), new Vector3(1.10f, 1.35f, 1.40f), new Vector3(1.40f, 1.55f, 1.45f),
                    new Vector3(1.00f, 1.15f, 1.20f) },
            new[] { new Vector3(1.00f, 1.00f, 1.00f), new Vector3(1.10f, 1.04f, 0.94f), new Vector3(0.88f, 0.90f, 0.94f),
                    new Vector3(1.18f, 1.12f, 1.02f), new Vector3(1.00f, 0.90f, 0.84f), new Vector3(0.78f, 0.78f, 0.80f) },
            new[] { new Vector3(1.00f, 1.00f, 1.00f), new Vector3(1.12f, 0.96f, 0.86f), new Vector3(0.78f, 0.74f, 0.74f),
                    new Vector3(1.20f, 1.12f, 0.98f), new Vector3(0.95f, 0.88f, 0.95f), new Vector3(0.88f, 0.70f, 0.62f) },
        };
        // The building being emitted: its column, its storey and where its
        // ground floor stands. Set by PickFacade, read by EmitFacadeQuad.
        static int facLook;
        static float facFloorH = OfficeFloorM, facBase;

        static uint FacadeHash(Vector2 p)
        {
            unchecked
            {
                uint h = (uint)Mathf.RoundToInt(p.x * 4f) * 0x9E3779B1u ^ (uint)Mathf.RoundToInt(p.y * 4f) * 0x85EBCA6Bu;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return h;
            }
        }

        /// <summary>
        /// Which column of the facade atlas a building wears, in what tint,
        /// with what storey (Uptown B1). OSM first: a tagged material picks
        /// the family (glass, brick, stone; metal reads as the silver grid),
        /// a tagged building:colour the tint (that colour over the column's
        /// own mean, in linear light). Otherwise by height class and hash -
        /// towers mostly glass in three kinds, midrises a third each of glass,
        /// precast and brick, the low blocks mostly brick - in a shade from
        /// the look's palette. Office floors are 3.8 m, homes 3.1 m (OSM
        /// building=apartments/residential/hotel, or brick with no use
        /// tagged), stretched so the top is a whole floor.
        /// Sets Bucket.Tint: call EndFacade after the building.
        /// </summary>
        static void PickFacade(Vector2 centre, byte style, byte use, byte mat, Color32 colour, float groundY, float height)
        {
            uint h = FacadeHash(centre);
            float r = (h & 0xFFFF) / 65536f;
            int glass = (int)((h >> 16) % 100u);
            int look;
            if (mat == 1 || mat == 4) look = mat == 4 ? LookSilver : (glass < 40 ? LookBlue : glass < 75 ? LookSilver : LookTeal);
            else if (mat == 2 || mat == 5) look = LookBrick;
            else if (mat == 3) look = LookStone;
            else if (style == 0) look = r < 0.12f ? LookStone : (glass < 38 ? LookBlue : glass < 72 ? LookSilver : LookTeal);
            else if (style == 1) look = r < 0.36f ? (glass < 45 ? LookBlue : glass < 80 ? LookSilver : LookTeal)
                                      : r < 0.70f ? LookStone : LookBrick;
            else look = r < 0.72f ? LookBrick : LookStone;
            facLook = look;

            bool home = use == 2 || (use == 0 && look == LookBrick);
            float nominal = home ? HomeFloorM : OfficeFloorM;
            float floors = Mathf.Max(1f, Mathf.Round(height / nominal));
            facFloorH = height > 0.5f ? height / floors : nominal;
            facBase = groundY;

            Vector3 k;
            if (colour.a == 255)
            {
                var m = FacadeLooks[look].meanLin;
                k = new Vector3(SrgbToLin(colour.r) / Mathf.Max(m.x, 0.02f),
                                SrgbToLin(colour.g) / Mathf.Max(m.y, 0.02f),
                                SrgbToLin(colour.b) / Mathf.Max(m.z, 0.02f));
            }
            else
            {
                var pal = LookPalette[look];
                k = pal[(int)((h >> 8) % (uint)pal.Length)];
            }
            Bucket.Tint = new Color32(TintByte(k.x), TintByte(k.y), TintByte(k.z), (byte)(look * 32));
        }

        static void EndFacade() => Bucket.Tint = new Color32(128, 128, 128, 0);

        static float SrgbToLin(byte c)
        {
            float v = c / 255f;
            return v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        }

        // x2 in the shader: 0.35..1.99 of the texel as painted
        static byte TintByte(float k) => (byte)Mathf.Clamp(Mathf.RoundToInt(k * 127.5f), 45, 254);

        // facade texture footprints in metres (how much wall one repeat covers)
        static readonly Vector2[] FacadeMeters =
        {
            new Vector2(9.5f, 12.5f),   // FacadeTower
            new Vector2(10.5f, 13.5f),  // FacadeMid
            new Vector2(6.5f, 6.5f),    // FacadeBrick
            new Vector2(24.0f, 4.2f),   // Shops (the atlas carries FOUR 6 m fronts per repeat)
            new Vector2(8.0f, 8.0f),    // FacadeGlass: 4x4 panes of 2 m
            new Vector2(6.0f, 3.1f),    // FacadeHouse: siding, one window per repeat, one storey tall
        };
        const float ShopFloorH = 4.2f;
        const float RoofTileM = 3.5f;
        const float RoofFlatM = 8f;

        /// <summary>One piece of a W-beam lead-in (leftover item 2), tile-local:
        /// the beam's FRONT line at road-edge height (<see cref="a"/>,
        /// <see cref="b"/>), outward (away from the traffic) unit vectors, and
        /// the beam's bottom and top at each end (absolute, tile-local y).</summary>
        public struct WBeamPiece
        {
            public Vector3 a, b;
            public Vector2 outA, outB;
            public float botA, topA, botB, topB;
            /// <summary>An end of the whole beam (its parapet end or its buried tip).</summary>
            public bool endA, endB;
        }
        /// <summary>One post of a W-beam lead-in: the beam's front point it
        /// carries (tile-local, road-edge height), outward, and the beam's top.</summary>
        public struct WBeamPost
        {
            public Vector3 at;
            public Vector2 outward;
            public float top;
        }

        public class SolidBox
        {
            public Vector3 center;   // tile-local
            public Vector3 size;
            public float yawDeg;
        }

        /// <summary>
        /// One street lamp, TILE-LOCAL like everything else here (see
        /// <see cref="PlaceLamps"/> for where and why). The tile's merged post
        /// mesh draws it, CityWorld.Attach stands a post collider on it, and
        /// CityWorld.EnsureTile hands every <see cref="head"/> to a NightGlow,
        /// which lights it after dark.
        /// </summary>
        public struct Lamp
        {
            /// <summary>The post's foot: the verge surface under it as the tile
            /// draws it, sunk <see cref="LampSinkM"/> so a sloped verge never
            /// shows daylight under a corner of the post.</summary>
            public Vector3 foot;
            /// <summary>The LENS: the underside of the luminaire at the end of
            /// the arm, over the road's edge. The light comes from here.</summary>
            public Vector3 head;
            /// <summary>Foot to lens, metres (the pole height by road class).</summary>
            public float height;
            /// <summary>0 a street lamp (classes 0-1), 1 an arterial's (2-4),
            /// 2 a freeway's (5), 3 uptown's acorn post (<see cref="LampAcorn"/>):
            /// which pitch and height it was placed by.</summary>
            public byte kind;
            /// <summary>It stands in a city race route's run-off
            /// (<see cref="RaceRunOff"/>) and BREAKS AWAY (Q15's pattern, as
            /// the business cabinets and the thin trunks do): drawn and lit,
            /// no collider. The WP-15 review.</summary>
            public bool breakaway;
        }

        /// <summary>Leftover item 6: a procedural house as a tile drew it.
        /// kind 1 a real footprint's gabled box, 2 a real house polygon (its
        /// box), 3 a frontage gable, 4 a fill house; y0 the bottom of its
        /// walls, floor where its siding meets the ground it was seated on.</summary>
        public struct HouseSeat { public byte kind; public Vector2 c, u; public float hu, hv, y0, floor; }

        public class TileMeshes
        {
            public Vector3 origin;
            /// <summary>The lattice AND the exact verge surfaces beside every
            /// grounded edge (and the solid ground behind retaining walls):
            /// one collider, ground grip, world-planar UVs so a verge and the
            /// cell under it paint the same texels where they meet.</summary>
            public Mesh ground;     public Slot[] groundSlots;
            public Mesh roads;      public Slot[] roadSlots;
            /// <summary>Concrete, its own collider on the Solid layer: Jersey
            /// barriers, retaining walls, and every rail (deck, approach,
            /// warranted drop, fan chord, gore nose).</summary>
            public Mesh barriers;
            /// <summary>Render-only: the shallow faces under grounded edges
            /// and junction fans. No collider, by design.</summary>
            public Mesh kerbs;
            public Mesh water;
            /// <summary>WP-25: the creeks' clay banks, draped on the lattice
            /// (render-only, the kit's bank material).</summary>
            public Mesh banks;
            /// <summary>Every facade and roof on the tile. It is its own
            /// collider now: a footprint's oriented box reached into the
            /// street wherever the footprint was not a rectangle, and an
            /// invisible wall across a lane is exactly what that felt like.</summary>
            public Mesh buildings;  public Slot[] buildingSlots;
            /// <summary>Piers. Buildings collide as their own mesh.</summary>
            public List<SolidBox> solids = new List<SolidBox>();
            /// <summary>The street lamps this tile OWNS (<see cref="PlaceLamps"/>),
            /// and their posts, arms and heads as one render-only mesh (null
            /// where there are none). The post colliders are CityWorld.Attach's,
            /// from this list.</summary>
            public List<Lamp> lamps = new List<Lamp>();
            public Mesh lampPosts;
            /// <summary>For the audit: lamp stations this tile owned, and why
            /// each one that stood no lamp was refused, indexed by the
            /// LampReject codes (<see cref="LampRejectNames"/>).</summary>
            public int lampStations;
            public readonly int[] lampRejects = new int[LampRejectCount];
            /// <summary>Walls the emitter caught pointing the wrong way. Zero,
            /// or the audit fails the build.</summary>
            public int wallFacingErrors;
            public int footprintCount, houseCount, goreCount, patchCount, branchCount;
            /// <summary>Metres of retaining (cut) wall this tile stood on an
            /// outside edge (<see cref="InCut"/>), for the audit's
            /// TerrainFidelity: real hills must not wall every hillside.</summary>
            public float cutWallM;
            /// <summary><see cref="cutWallM"/> by SideFlags.cutWhy (WP-14).</summary>
            public readonly float[] cutWallByWhy = new float[4];
            /// <summary><see cref="cutWallM"/> by edge, with the wall's
            /// reason and a point on it (the audit lists the runs).</summary>
            public readonly Dictionary<int, (float m, byte why, Vector2 at)> cutWallByEdge = new Dictionary<int, (float, byte, Vector2)>();
            /// <summary>Footprints this tile cut back off the drawn pavement,
            /// and those it left out (<see cref="FitFootprint"/>).</summary>
            public int footprintsCut, footprintsLeftOut;
            /// <summary>For the audits: every verge span this tile laid
            /// (edge, side -1 L / +1 R, arc range), and every gore nose it
            /// owns (world-space chord, the forward direction past it, and
            /// whether it is on structure).</summary>
            public readonly List<(int edge, int side, float s0, float s1)> vergeSpans = new List<(int, int, float, float)>();
            public readonly List<(Vector3 a, Vector3 b, Vector3 forward, bool elevated)> goreNoses = new List<(Vector3, Vector3, Vector3, bool)>();
            public float vergeMetres, railMetres;
            /// <summary>Leftover item 2: the W-beam lead-ins this tile owns
            /// (tile-local), drawn with the tile's furniture by
            /// CityPoles.Build; <see cref="guardrails"/> is their collider.</summary>
            public readonly List<WBeamPiece> wbeams = new List<WBeamPiece>();
            public readonly List<WBeamPost> wbeamPosts = new List<WBeamPost>();
            /// <summary>COLLIDER ONLY (Solid layer, CityWorld.Attach names it
            /// "Guardrail"): the W-beams, a closed box from the beam's face
            /// back past its posts. Null where the tile has none.</summary>
            public Mesh guardrails;
            /// <summary>For the audit: rail run ends this tile decided, by
            /// treatment (W-beam lead-in, sloped past the end, sloped inside).</summary>
            public int railEndsWBeam, railEndsSloped, railEndsInside, railEndsConnected, railEndsKept, railEndsShort;
            /// <summary>The smoothness gate's tap; null unless <see cref="RecordTap"/>.</summary>
            public RoadTap tap;
            /// <summary>The fill houses BuildHouses stood (world plan: centre,
            /// unit long axis, half extents), for the roadside occupancy mask
            /// (WP-08): a tree must not grow through one.</summary>
            public readonly List<(Vector2 c, Vector2 u, float hu, float hv)> houseBoxes = new List<(Vector2, Vector2, float, float)>();
            /// <summary>Leftover item 6: every procedural HOUSE this tile drew
            /// (a real footprint's gabled box or house polygon, a frontage
            /// gable, a fill house) as drawn - its oriented box and the bottom
            /// of its walls - for the house audit (gaps, buried fronts).</summary>
            public readonly List<HouseSeat> houseSeats = new List<HouseSeat>();
            /// <summary>WP-25: the culvert ends this tile stood (world plan:
            /// the wall's face on the ravine, the outward direction, the
            /// backfill's depth behind it), for the occupancy mask and the
            /// audit.</summary>
            public readonly List<CulvertEnd> culvertEnds = new List<CulvertEnd>();
        }

        // ---- the smoothness gate's tap (Editor/CitySmooth.cs, gate spec 5.1) ----
        /// <summary>Set only by the editor's smoothness gate. While it is set
        /// every tile build records WHERE its ribbon quads and fans went, so the
        /// gate can read them back out of <see cref="TileMeshes.roads"/> - the
        /// mesh the renderer draws. One branch per quad when false; nothing in
        /// the game sets it.</summary>
        public static bool RecordTap;
        public sealed class RoadTap
        {
            /// <summary>A ribbon quad: its slot, the index of its first vertex
            /// in that slot's bucket (A.L, B.L, B.R, A.R follow), the edge, the
            /// two sections' arcs and flags (<see cref="TapFlags"/>). A span
            /// drawn in PAINT COLUMNS (WP-11b) is <c>strips</c> such quads in a
            /// row from the L edge to the R edge, four vertices each: the
            /// first's A.L / B.L and the last's B.R / A.R are the ribbon's edges.</summary>
            public struct Span { public int slot, bucketV, edge; public float sA, sB; public ushort flagsA, flagsB; public int strips; }
            /// <summary>A junction fan: centre at bucketV, then count - 1
            /// corners anticlockwise; bit i of mouths = the chord from corner i
            /// to i + 1 is a road mouth. Its triangles are triCount of the
            /// slot's, from index triStart of its list (a star from the centre,
            /// or ear-clipped corners when they are not star-shaped).</summary>
            public struct Fan { public int slot, bucketV, count, node, triStart, triCount; public ulong mouths; }
            public readonly List<Span> spans = new List<Span>();
            public readonly List<Fan> fans = new List<Fan>();
            /// <summary>Gore quads (junction-slab slot, no paint): pavement for
            /// the gate's D1. sA/sB are arcs on the HOST edge.</summary>
            public readonly List<Span> gores = new List<Span>();
            /// <summary>tm.roads' vertex index of each used slot's first vertex,
            /// in tm.roadSlots order (MeshFrom concatenates the buckets).</summary>
            public int[] slotBase;
            public const ushort FSqueezedL = 1, FSqueezedR = 2, FClipL = 4, FClipR = 8, FCollapsed = 16, FElevated = 32;
        }
        static int[] tapSlotBase;
        /// <summary>The section's CAUSES, never its symptoms: squeezed L/R -
        /// SqueezeSection moved that edge in against a parallel neighbour (the
        /// gate judges it against its I7 envelope; an edge that stands inside
        /// its half width for any other reason is a regression the gate must
        /// see against the design edge, so the drawn position must not decide
        /// this: review 5); clip: the inner side is the host's edge; collapsed;
        /// on structure. linesim.mjs sets its sqL / sqR the same way.</summary>
        static ushort TapFlags(in Section c)
        {
            int f = (c.sqL ? RoadTap.FSqueezedL : 0) | (c.sqR ? RoadTap.FSqueezedR : 0)
                  | (c.clippedIn && c.innerSide < 0 ? RoadTap.FClipL : 0) | (c.clippedIn && c.innerSide > 0 ? RoadTap.FClipR : 0)
                  | (c.collapsed ? RoadTap.FCollapsed : 0) | (c.elev ? RoadTap.FElevated : 0);
            return (ushort)f;
        }

        // ---- growable buckets, one per slot, reused across tiles ----------
        class Bucket
        {
            public List<Vector3> v = new List<Vector3>(512);
            public List<Vector2> uv = new List<Vector2>(512);
            public List<int> t = new List<int>(1024);
            /// <summary>Uptown B1: per-vertex colour, kept by the facade
            /// atlas's bucket alone (null everywhere else). Quad and Tri
            /// stamp <see cref="Tint"/> on every vertex they add.</summary>
            public List<Color32> col;
            public static Color32 Tint = new Color32(128, 128, 128, 0);
            public void Clear() { v.Clear(); uv.Clear(); t.Clear(); col?.Clear(); }
            public int Count => v.Count;

            /// <summary>Raw quad: emits (a,c,b)+(a,d,c). Shows the side from
            /// which a→b→c→d reads anticlockwise. Prefer <see cref="Up"/>,
            /// <see cref="Down"/> and <see cref="Wall"/>, which decide that
            /// for you.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                             Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
                if (col != null) { col.Add(Tint); col.Add(Tint); col.Add(Tint); col.Add(Tint); }
                t.Add(i); t.Add(i + 2); t.Add(i + 1);
                t.Add(i); t.Add(i + 3); t.Add(i + 2);
            }

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c);
                uv.Add(ua); uv.Add(ub); uv.Add(uc);
                if (col != null) { col.Add(Tint); col.Add(Tint); col.Add(Tint); }
                t.Add(i); t.Add(i + 2); t.Add(i + 1);
            }

            /// <summary>A horizontal-ish quad that faces UP whatever order
            /// its corners arrive in: the map-view signed area decides.</summary>
            public void Up(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                           Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                float area = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z)
                           + (c.x - a.x) * (d.z - a.z) - (d.x - a.x) * (c.z - a.z);
                if (area >= 0f) Quad(a, b, c, d, ua, ub, uc, ud);
                else Quad(a, d, c, b, ua, ud, uc, ub);
            }

            /// <summary>A horizontal-ish quad that faces DOWN: a soffit.</summary>
            public void Down(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                             Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                float area = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z)
                           + (c.x - a.x) * (d.z - a.z) - (d.x - a.x) * (c.z - a.z);
                if (area < 0f) Quad(a, b, c, d, ua, ub, uc, ud);
                else Quad(a, d, c, b, ua, ud, uc, ub);
            }

            /// <summary>A vertical quad between two plan points, facing the
            /// side <paramref name="outward"/> points to. u runs along the
            /// wall from p to q, v up.</summary>
            public void Wall(Vector3 p, Vector3 q, float y0, float y1, Vector2 outward,
                             float u0, float u1, float v0, float v1)
            {
                // a wall from a to c faces the LEFT of a→c in map view
                Vector2 d = new Vector2(q.x - p.x, q.z - p.z);
                Vector2 left = new Vector2(-d.y, d.x);
                if (Vector2.Dot(left, outward) < 0f) { var t = p; p = q; q = t; var tu = u0; u0 = u1; u1 = tu; }
                Quad(new Vector3(p.x, y0, p.z), new Vector3(p.x, y1, p.z),
                     new Vector3(q.x, y1, q.z), new Vector3(q.x, y0, q.z),
                     new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0));
            }

            /// <summary>A vertical quad whose two ends stand at DIFFERENT
            /// heights (a kerb along a grade, a deck fascia): p's face spans
            /// pY0..pY1, q's spans qY0..qY1. Faces <paramref name="outward"/>.</summary>
            public void WallSloped(Vector3 p, Vector3 q, float pY0, float pY1, float qY0, float qY1,
                                   Vector2 outward, float u0, float u1, float v0, float v1)
            {
                Vector2 d = new Vector2(q.x - p.x, q.z - p.z);
                Vector2 left = new Vector2(-d.y, d.x);
                if (Vector2.Dot(left, outward) < 0f)
                {
                    var t = p; p = q; q = t;
                    var tu = u0; u0 = u1; u1 = tu;
                    var t0 = pY0; pY0 = qY0; qY0 = t0;
                    var t1 = pY1; pY1 = qY1; qY1 = t1;
                }
                Quad(new Vector3(p.x, pY0, p.z), new Vector3(p.x, pY1, p.z),
                     new Vector3(q.x, qY1, q.z), new Vector3(q.x, qY0, q.z),
                     new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0));
            }

            /// <summary>A quad of any orientation facing the side
            /// <paramref name="facing"/> points to: a rail's end cap, a seal
            /// face. Quad(a,b,c,d) draws (a,c,b), whose normal is
            /// Cross(c - a, b - a).</summary>
            public void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing,
                             Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                if (Vector3.Dot(Vector3.Cross(c - a, b - a), facing) >= 0f) Quad(a, b, c, d, ua, ub, uc, ud);
                else Quad(a, d, c, b, ua, ud, uc, ub);
            }

            /// <summary>Normal of the most recent triangle, for the emitter's
            /// own facing check.</summary>
            public Vector3 LastNormal()
            {
                int n = t.Count;
                if (n < 3) return Vector3.zero;
                Vector3 a = v[t[n - 3]], b = v[t[n - 2]], c = v[t[n - 1]];
                return Vector3.Cross(b - a, c - a);
            }
        }

        static readonly Bucket[] buckets = NewBuckets();
        static readonly Bucket barrierBucket = new Bucket();
        /// <summary>Leftover item 2: the W-beams' collider, never drawn.</summary>
        static readonly Bucket guardBucket = new Bucket();
        static readonly Bucket kerbBucket = new Bucket();
        /// <summary>The lamp posts, arms and heads: their own mesh, drawn with
        /// CityWorld's runtime post material rather than a Slot (a new Slot
        /// member shifts RoadFirst and misaligns every baked city scene's
        /// materials array until all four are rebuilt).</summary>
        static readonly Bucket lampBucket = new Bucket();
        static Bucket[] NewBuckets()
        {
            var b = new Bucket[(int)Slot.COUNT];
            for (int i = 0; i < b.Length; i++) b[i] = new Bucket();
            b[(int)Slot.FacadeGlass].col = new List<Color32>(512);   // the facade atlas (Uptown B1)
            return b;
        }

        static readonly HashSet<int> segScratch = new HashSet<int>();
        static readonly HashSet<int> edgeScratch = new HashSet<int>();
        static readonly List<Vector2> polyScratch = new List<Vector2>();

        // ==================================================================
        //  Junction geometry, decided once per map.
        // ==================================================================

        /// <summary>
        /// What every edge end does at its node, computed once from the graph
        /// (<see cref="ComputeTrims"/>) and read by every tile build.
        /// </summary>
        public class Trims
        {
            /// <summary>Ribbon trim at each end of each edge: the ribbon runs
            /// from atA to length - atB, the fan patch covers the rest.</summary>
            public float[] atA, atB;
            /// <summary>RETIRED with the symmetric taper (WP-11b): the line
            /// model's one-sided eases (<see cref="LineModel"/>) replace them.
            /// Kept, at the plain half width and no taper, for readers that
            /// still index them.</summary>
            public float[] hwA, hwB;
            public float[] taperA, taperB;
            /// <summary>The THROUGH edge this end's ribbon is clipped against
            /// (a ramp at its merge, the minor branch of a fork), or -1.</summary>
            public int[] branchA, branchB;
            /// <summary>The node draws a fan patch between its arms.</summary>
            public bool[] patch;
            /// <summary>The node is a plain continuation (two arms, or a
            /// through pair with clipped branches): the through ribbons meet
            /// there with a MITRED cross-section.</summary>
            public bool[] mitre;
            /// <summary>For a mitred node: the two edges that meet through it.</summary>
            public int[] throughA, throughB;
            /// <summary>Roads pass L8 (plan A17): a dead end OSM tags as a
            /// turning circle draws a BULB - a one-arm fan round a circle of
            /// <see cref="bulbRad"/> about the node.</summary>
            public bool[] bulb;
            public float[] bulbRad;
            /// <summary>Leftover item 4: the curb-return radius of a bulb's
            /// neck (where the street meets its circle), 0 where it has none.</summary>
            public float[] bulbNeckR;
            /// <summary>Leftover item 4: a bulb's centre - the node, or its foot
            /// on the mouth's line where the street bends into the bulb.</summary>
            public Vector2[] bulbC;
            /// <summary>Plan A2: each edge's twin-deck union runs (null where it
            /// has none), from <see cref="BuildDeckUnions"/>.</summary>
            public List<UnionRun>[] unions;
            /// <summary>Plan L7 / A11: each node's junction cluster (index into
            /// <see cref="clusters"/>, or -1), the clusters, and the edges
            /// inside one (no ribbon: the cluster's ring paves them).</summary>
            public int[] clusterOf;
            public List<JunctionCluster> clusters;
            public bool[] internalEdge;
            /// <summary>Leftover item 3: each junction's MAIN ARM, by its
            /// <see cref="FanKey"/> node - the edge whose surface (material
            /// and age) the paved junction takes - the member node it leaves,
            /// and its through partner (-1: none). <see cref="FanMainArms"/>.</summary>
            public int[] fanMain, fanMainNode, fanPartner;

            public JunctionCluster ClusterOfNode(int n) =>
                clusterOf != null && n >= 0 && n < clusterOf.Length && clusterOf[n] >= 0 ? clusters[clusterOf[n]] : null;
            public bool Internal(int edge) => internalEdge != null && edge >= 0 && edge < internalEdge.Length && internalEdge[edge];

            public float TrimAt(CityMap.Edge e, int node) => e.a == node ? atA[e.index] : atB[e.index];
            public int BranchAt(CityMap.Edge e, int node) => e.a == node ? branchA[e.index] : branchB[e.index];

            /// <summary>The ribbon's HALF WIDTH at an arc position, tapers
            /// included (the line model: it is centred
            /// <see cref="CentreAt"/> off the OSM line, not on it).</summary>
            public float HalfWidthAt(CityMap.Edge e, float s)
            {
                LineModel.Extents(e, s, out float eM, out float eP);
                return 0.5f * (eM + eP);
            }
            /// <summary>The ribbon's extents off the OSM line: eMinus to the L
            /// vertex (p - right * eMinus), ePlus to the R vertex.</summary>
            public void ExtentsAt(CityMap.Edge e, float s, out float eMinus, out float ePlus) =>
                LineModel.Extents(e, s, out eMinus, out ePlus);
            /// <summary>The ribbon's centre off the OSM line along right (the
            /// left of a->b) at s.</summary>
            public float CentreAt(CityMap.Edge e, float s)
            {
                LineModel.Extents(e, s, out float eM, out float eP);
                return 0.5f * (eP - eM);
            }
            /// <summary>How far the pavement reaches off the OSM line on
            /// whichever side is further: a clearance bound.</summary>
            public float ReachAt(CityMap.Edge e, float s)
            {
                LineModel.Extents(e, s, out float eM, out float eP);
                return Mathf.Max(eM, eP);
            }
        }

        /// <summary>The direction an edge LEAVES a node in.</summary>
        static Vector2 OutDir(CityMap.Edge e, int node) =>
            e.a == node ? e.TangentAt(0f) : -e.TangentAt(e.length);

        /// <summary>PSX_CITY_ARMTRIM=1: each arm of a fan trimmed only as far as
        /// its own ribbon needs (plan A3 FIX-3). OFF: L3's first audit
        /// (2026-10-03) measured it in the OwnerBox - coplanar arm/arm T1
        /// 127 -> 183 m2 and fan over its own arm 27 -> 74 m2, and two lane
        /// mouths at node 13336 (South Caldwell / East Morehead) on another
        /// arm 0.4 m off. Without A3's chord arm directions (FIX-2) a curving
        /// arm needs more than the straight-arm formula gives. For L7.</summary>
        public static bool PerArmTrims = System.Environment.GetEnvironmentVariable("PSX_CITY_ARMTRIM") == "1";
        static readonly List<float> armTrim = new List<float>(8);
        /// <summary>How far an arm's ribbon reaches off its OSM line on the
        /// left (<paramref name="left"/>) or right of its direction OUT of
        /// <paramref name="node"/>: the line model's extents (lmPlus is the
        /// left of a to b).</summary>
        static float ArmExt(CityMap.Edge e, int node, bool left)
        {
            if (e.lmPlus == 0f && e.lmMinus == 0f) return e.width * 0.5f;
            bool fromA = e.a == node;
            return left == fromA ? e.lmPlus : e.lmMinus;
        }

        const float ThroughCos = -0.85f;    // arms this opposite are one road going through
        /// <summary>Leftover item 3: PSX_CITY_CROSSINGS=0 draws a road crossing
        /// another at a skew as before (a merge and a diverge, clipped).</summary>
        public static bool CrossingsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_CROSSINGS") != "0";
        /// <summary>The two halves of a crossing road go on through each other
        /// (within 45 degrees of straight).</summary>
        const float CrossingPairCos = -0.7f;
        /// <summary>A crossing's acute pairs are trimmed by their true overlap
        /// down to 15 degrees (0.5 = 30 degrees elsewhere).</summary>
        const float CrossingMinSin = 0.26f;
        /// <summary>Nodes the last ComputeTrims drew as crossings.</summary>
        public static int CrossingNodes { get; private set; }
        /// <summary>Arms closer than this in direction are CLIPPED against
        /// each other rather than trimmed: a ramp beside its mainline, the
        /// minor arm of a fork, a side street meeting a road at 50 degrees.
        /// Sixty degrees. Below that a fan's corner cones overlap and its
        /// triangles fold over the arms; a clipped mouth is exact.</summary>
        const float BranchCos = 0.5f;
        const float ContinueCos = -0.906f;  // a two-arm node bending less than 25 deg is a bend in the ribbon

        /// <summary>Kept for callers that only ever wanted a per-node
        /// "is there a patch here" — the full table is <see cref="ComputeTrims"/>.</summary>
        public static Trims NodeTrims(CityMap map) => ComputeTrims(map);

        public static Trims ComputeTrims(CityMap map)
        {
            int ne = map.edges.Length, nn = map.nodes.Length;
            var t = new Trims
            {
                atA = new float[ne], atB = new float[ne],
                hwA = new float[ne], hwB = new float[ne],
                taperA = new float[ne], taperB = new float[ne],
                branchA = new int[ne], branchB = new int[ne],
                patch = new bool[nn], mitre = new bool[nn],
                throughA = new int[nn], throughB = new int[nn],
            };
            for (int i = 0; i < ne; i++)
            {
                t.hwA[i] = t.hwB[i] = map.edges[i].width * 0.5f;
                t.branchA[i] = t.branchB[i] = -1;
            }
            for (int i = 0; i < nn; i++) { t.throughA[i] = t.throughB[i] = -1; }

            var arms = new List<(CityMap.Edge e, Vector2 dir, float hw)>(8);
            var clipped = new List<int>(8);     // arm index -> arm it clips against, or -1
            int crossingCount = 0;
            var crossFlags = new bool[nn];
            var joins = new List<(int node, int e, int o)>(1024);
            for (int n = 0; n < nn; n++)
            {
                var list = map.nodeEdges[n];
                if (list.Count < 2) continue;
                arms.Clear();
                foreach (var ei in list)
                {
                    var e = map.edges[ei];
                    if (e.a == e.b) continue;
                    arms.Add((e, OutDir(e, n), e.width * 0.5f));
                }
                if (arms.Count < 2) continue;

                // The through pair: the two arms that most nearly continue
                // each other, mainline first (a ramp is never the through road).
                int tA = -1, tB = -1; float best = 1f;
                for (int pass = 0; pass < 2 && tA < 0; pass++)
                    for (int i = 0; i < arms.Count; i++)
                        for (int j = i + 1; j < arms.Count; j++)
                        {
                            if (pass == 0 && (arms[i].e.link || arms[j].e.link)) continue;
                            float d = Vector2.Dot(arms[i].dir, arms[j].dir);
                            if (d < best) { best = d; tA = i; tB = j; }
                        }
                bool through = tA >= 0 && best < ThroughCos;

                // Where the lane count (or the shoulder) changes at a mitred
                // node - a plain continuation OR a merge, where the mainline
                // gains or loses its auxiliary lane - the LINE MODEL eases the
                // one side that steps (WP-11b, plan A8: never both edges; the
                // symmetric taper, clamped to 0.9 of one OSM piece, is gone).
                void Taper((CityMap.Edge e, Vector2 dir, float hw) a0, (CityMap.Edge e, Vector2 dir, float hw) a1) =>
                    joins.Add((n, a0.e.index, a1.e.index));

                if (arms.Count == 2)
                {
                    if (best < ContinueCos)
                    {
                        // A continuation: mitred ends.
                        t.mitre[n] = true;
                        t.throughA[n] = arms[0].e.index; t.throughB[n] = arms[1].e.index;
                        Taper(arms[0], arms[1]);
                        continue;
                    }
                    // a real bend: a small fan fills the outside corner
                }

                // Shallow pairs are CLIPPED, not trimmed: the arm that is a
                // link, or narrower, or of the lower class hugs the other.
                clipped.Clear();
                for (int i = 0; i < arms.Count; i++) clipped.Add(-1);
                for (int i = 0; i < arms.Count; i++)
                    for (int j = i + 1; j < arms.Count; j++)
                    {
                        if (Vector2.Dot(arms[i].dir, arms[j].dir) < BranchCos) continue;
                        var ai = arms[i]; var aj = arms[j];
                        bool iClips;
                        if (ai.e.link != aj.e.link) iClips = ai.e.link;
                        else if (ai.e.cls != aj.e.cls) iClips = ai.e.cls < aj.e.cls;
                        else if (Mathf.Abs(ai.hw - aj.hw) > 0.05f) iClips = ai.hw < aj.hw;
                        else iClips = true;
                        if (through && (i == tA || i == tB) && (j == tA || j == tB)) continue;
                        if (through && (j == tA || j == tB)) iClips = true;
                        if (through && (i == tA || i == tB)) iClips = false;
                        int c = iClips ? i : j, h = iClips ? j : i;
                        if (clipped[c] < 0) clipped[c] = h;
                    }

                // A ROAD CROSSING AT GRADE (leftover item 3; the owner, Little
                // Rock Road under I-85: "a mess"): a branch clipped beside the
                // arm coming in and another beside the arm going out, on the
                // two SIDES of the through road and going on through each
                // other, are one road crossing this one at a skew - the left
                // turns and ramp traffic across Little Rock Road's carriageways -
                // and not a merge and a diverge. Clipped, each half hugged the
                // through road: its edge lines ran on across the other road's
                // lanes and wedges of one ribbon lay over the other. A crossing
                // is a junction: no arm clipped, every arm trimmed clear of the
                // others (the acute pairs by their true overlap).
                // Only a RAMP OR TURNING ROADWAY crossing (both halves links): the
                // first audit also drew street-over-street skews this way (Dalton
                // Avenue x North Graham, East 12th x North Caldwell) and doubled
                // a cluster's grass hole there and stood a rail end in a lane
                // mouth; those keep the clip for now.
                bool crossing = false;
                // not on a bridge: Tyvola Road's links cross it ON its deck over
                // I-77, where the fan's chords are deck edges (the first audit:
                // parapet faces at the lane mouths); there the clip stays
                bool onBridge = false;
                foreach (var a in arms) if (a.e.bridge) { onBridge = true; break; }
                if (through && CrossingsOn && !onBridge)
                {
                    var fwd = -arms[tA].dir;
                    for (int i = 0; i < arms.Count && !crossing; i++)
                    {
                        if (i == tA || i == tB || clipped[i] != tA) continue;
                        for (int j = 0; j < arms.Count; j++)
                        {
                            if (j == tA || j == tB || j == i || clipped[j] != tB) continue;
                            if (!arms[i].e.link || !arms[j].e.link) continue;   // a ramp or turning roadway crossing (see above)
                            if (Vector2.Dot(arms[i].dir, arms[j].dir) > CrossingPairCos) continue;
                            float si = fwd.x * arms[i].dir.y - fwd.y * arms[i].dir.x;
                            float sj = fwd.x * arms[j].dir.y - fwd.y * arms[j].dir.x;
                            if (si * sj >= 0f) continue;
                            crossing = true;
                            break;
                        }
                    }
                    // every arm a junction arm: Little Rock Road's ramps cross its
                    // carriageways at 19-21 deg, and their two pavements overlap for
                    // 30 m before the crossing point - all of it the junction's
                    if (crossing) { for (int i = 0; i < arms.Count; i++) clipped[i] = -1; crossingCount++; crossFlags[n] = true; }
                }

                // A through road with nothing but branches beside it draws
                // no patch: the through ribbons meet mitred, the branches
                // clip. Anything else is a fan.
                bool allBranch = through;
                if (through)
                    for (int i = 0; i < arms.Count && allBranch; i++)
                        if (i != tA && i != tB && clipped[i] < 0) allBranch = false;
                if (allBranch)
                {
                    t.mitre[n] = true;
                    t.throughA[n] = arms[tA].e.index; t.throughB[n] = arms[tB].e.index;
                    Taper(arms[tA], arms[tB]);
                    for (int i = 0; i < arms.Count; i++)
                    {
                        if (clipped[i] < 0) continue;
                        var e = arms[i].e;
                        if (e.a == n) t.branchA[e.index] = arms[clipped[i]].e.index;
                        else t.branchB[e.index] = arms[clipped[i]].e.index;
                    }
                    continue;
                }

                // A fan. Every arm is trimmed back by the same distance: as
                // far as the most-overlapping pair demands. Two ribbons from
                // one node at angle theta overlap until r sin(theta) exceeds
                // hw_j + hw_i cos(theta); trimming to that also keeps the
                // corner cones from interleaving in the angular sort, which
                // is what folded fan triangles over the arms. Arms going
                // straight through need nothing from each other, and a pair
                // shallower than BranchCos is clipped instead.
                t.patch[n] = true;
                float trimN = 0f;
                armTrim.Clear();
                for (int i = 0; i < arms.Count; i++) armTrim.Add(0f);
                for (int i = 0; i < arms.Count; i++)
                    for (int j = 0; j < arms.Count; j++)
                    {
                        if (j == i) continue;
                        float d = Vector2.Dot(arms[i].dir, arms[j].dir);
                        if (d < ThroughCos) continue;                       // straight through: no overlap
                        if (clipped[i] == j || clipped[j] == i) continue;   // handled by clipping
                        float sin = Mathf.Max(crossing ? CrossingMinSin : 0.5f, Mathf.Sqrt(Mathf.Max(0f, 1f - d * d)));
                        // the reach off the OSM line on the further side: an
                        // arm offset by the line model overlaps by that much more
                        trimN = Mathf.Max(trimN, (arms[j].e.HalfMax + arms[i].e.HalfMax * Mathf.Abs(d) + 0.6f) / sin);
                        // PER ARM (plan L3 / A3 FIX-3): arm i is trimmed only as
                        // far as ITS ribbon needs to clear arm j's, by the two
                        // sides that face each other: j's half towards i, i's
                        // half towards j. Never more than the old common trim.
                        float cr = arms[i].dir.x * arms[j].dir.y - arms[i].dir.y * arms[j].dir.x;   // > 0: j lies left of i
                        float exI = ArmExt(arms[i].e, n, cr > 0f), exJ = ArmExt(arms[j].e, n, cr < 0f);
                        float need = d >= 0f ? (exJ + exI * d + 0.6f) / sin : Mathf.Max(0.6f, (exJ + exI * d) / sin);
                        armTrim[i] = Mathf.Max(armTrim[i], need);
                    }
                for (int i = 0; i < arms.Count; i++)
                {
                    var e = arms[i].e;
                    float trim = Mathf.Min(PerArmTrims ? Mathf.Min(armTrim[i], trimN) : trimN, e.length * 0.49f);
                    if (e.a == n) t.atA[e.index] = trim; else t.atB[e.index] = trim;
                    if (clipped[i] >= 0)
                    {
                        if (e.a == n) t.branchA[e.index] = arms[clipped[i]].e.index;
                        else t.branchB[e.index] = arms[clipped[i]].e.index;
                    }
                }
            }

            CrossingNodes = crossingCount;
            CrossingNodeFlags = crossFlags;
            // roads pass L7 (plan A10): the curb returns' trims
            var medianX = JunctionTrims(map, t);
            // roads pass L8 (plan A17): the cul-de-sac bulbs
            BulbTrims(map, t);

            // An edge shorter than its two trims has no ribbon: the two fans
            // meet in the middle, exactly, instead of leaving a sliver.
            for (int i = 0; i < ne; i++)
            {
                var e = map.edges[i];
                float sum = t.atA[i] + t.atB[i];
                if (sum > e.length - 0.6f && sum > 0f)
                {
                    float k = e.length / sum;
                    t.atA[i] *= k; t.atB[i] *= k;
                }
            }
            // roads pass L7 (plan A11): one paved area per junction cluster
            BuildJunctionClusters(map, t, medianX);
            ChainContinuity(map, t);
            // the line model's one-sided tapers, run on through mitred joints
            LineModel.BuildEases(map, joins, (e, node) =>
            {
                if (!t.mitre[node]) return -1;
                int o = t.throughA[node] == e.index ? t.throughB[node] : t.throughB[node] == e.index ? t.throughA[node] : -1;
                return o == e.index ? -1 : o;
            });
            // roads pass L5: the merge zones (aux lanes on the hosts, the ramps cut at N)
            BuildMergeZones(map, t);
            // leftover item 4: the bulbs' necks, on the street's final extents
            BulbNecks(map, t);
            // plan A2: which parallel decks are drawn as one structure, where
            BuildDeckUnions(map, t);
            return t;
        }

        /// <summary>CHAIN CONTINUITY (plan WP-11): walk every chain of edges
        /// joined through mitred THROUGH joints and give each edge its texture
        /// V offset and direction along it (V = (vOff + vDir s) / RoadVTile, so
        /// the dash phase runs on through way splits, decks and seams), and one
        /// surface-age seed per chain (the head edge's first point). A closed
        /// ring starts anywhere; its one joint back to the start keeps a phase
        /// step, which a ring of a whole number of cycles would not.</summary>
        static void ChainContinuity(CityMap map, Trims t)
        {
            int ne = map.edges.Length;
            var seen = new bool[ne];
            var chain = new List<int>(64);
            var fwds = new List<bool>(64);
            int Partner(CityMap.Edge e, int node)
            {
                if (!t.mitre[node]) return -1;
                int o = t.throughA[node] == e.index ? t.throughB[node] : t.throughB[node] == e.index ? t.throughA[node] : -1;
                if (o < 0 || o == e.index || map.edges[o].a == map.edges[o].b) return -1;
                return o;
            }
            // the chain's phase must not depend on where the walk began (edge order):
            // an open chain starts at its end with the lower (x, z), a ring at its
            // joint with the lower (x, z)
            bool Lower(Vector2 p, Vector2 q) => p.x < q.x || (p.x == q.x && p.y < q.y);
            Vector2 EntryPt(int ei, bool fwd) { var e = map.edges[ei]; return fwd ? e.pts[0] : e.pts[e.pts.Length - 1]; }
            Vector2 ExitPt(int ei, bool fwd) { var e = map.edges[ei]; return fwd ? e.pts[e.pts.Length - 1] : e.pts[0]; }
            for (int i = 0; i < ne; i++)
            {
                var e0 = map.edges[i];
                if (seen[i] || e0.a == e0.b) continue;
                // back to the chain's head: the chain is walked a -> b on e0
                int cur = i, entry = e0.a, guard = 0;
                bool ring = false;
                for (;;)
                {
                    int p = Partner(map.edges[cur], entry);
                    if (p == i) { ring = true; break; }
                    if (p < 0 || seen[p] || ++guard > ne) break;
                    var pe = map.edges[p];
                    entry = pe.a == entry ? pe.b : pe.a;
                    cur = p;
                }
                if (ring) { cur = i; entry = e0.a; }
                chain.Clear(); fwds.Clear();
                guard = 0;
                while (cur >= 0 && !seen[cur] && ++guard <= ne)
                {
                    var e = map.edges[cur];
                    seen[cur] = true;
                    bool fwd = entry == e.a;
                    chain.Add(cur); fwds.Add(fwd);
                    int exit = fwd ? e.b : e.a;
                    cur = Partner(e, exit);
                    entry = exit;
                }
                int n = chain.Count;
                if (ring)
                {
                    int best = 0;
                    for (int k = 1; k < n; k++) if (Lower(EntryPt(chain[k], fwds[k]), EntryPt(chain[best], fwds[best]))) best = k;
                    if (best > 0)
                    {
                        var c2 = new List<int>(n); var f2 = new List<bool>(n);
                        for (int k = 0; k < n; k++) { c2.Add(chain[(best + k) % n]); f2.Add(fwds[(best + k) % n]); }
                        chain.Clear(); chain.AddRange(c2); fwds.Clear(); fwds.AddRange(f2);
                    }
                }
                else if (Lower(ExitPt(chain[n - 1], fwds[n - 1]), EntryPt(chain[0], fwds[0])))
                {
                    chain.Reverse(); fwds.Reverse();
                    for (int k = 0; k < n; k++) fwds[k] = !fwds[k];
                }
                Vector2 seed = EntryPt(chain[0], fwds[0]);
                float acc = 0f;
                for (int k = 0; k < n; k++)
                {
                    var e = map.edges[chain[k]];
                    bool fwd = fwds[k];
                    e.vDir = fwd ? 1f : -1f;
                    e.vOff = Mathf.Repeat(fwd ? acc : acc + e.length, RoadVTile);
                    e.ageSeed = seed; e.hasAgeSeed = true;
                    acc = Mathf.Repeat(acc + e.length, RoadVTile);
                }
            }
            ShareSurfaceAges(map, t);
        }

        /// <summary>
        /// ONE SURFACE PER STRUCTURE (plan L3; the owner on the R1 top view of
        /// West 5th Street over I-77, 2026-10-02: "these don't make any
        /// sense"). The age (new or old, so light or brown concrete on a deck,
        /// black or grey asphalt on the ground) was chosen per CHAIN, and the
        /// two carriageways of one divided road are two chains: one bridge
        /// read as two, half light and half brown, and its approaches black
        /// beside grey. Every chain is now joined with the chain across the
        /// median - a union deck's partner (<see cref="CityMap.deckPairs"/>),
        /// and the opposite carriageway of any divided road (one-way, not a
        /// ramp, the same name, running the other way within
        /// <see cref="DividedReachM"/>) - and each joined group takes one seed
        /// (its lowest), so both halves are resurfaced together, as a real
        /// contract does both directions.
        /// </summary>
        static void ShareSurfaceAges(CityMap map, Trims t)
        {
            int ne = map.edges.Length;
            var parent = new int[ne];
            for (int i = 0; i < ne; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Join(int a, int b) { a = Find(a); b = Find(b); if (a != b) { if (a < b) parent[b] = a; else parent[a] = b; } }
            // a chain is one group already: its edges share the head's seed
            var bySeed = new Dictionary<Vector2, int>(ne);
            for (int i = 0; i < ne; i++)
            {
                var e = map.edges[i];
                if (!e.hasAgeSeed) continue;
                if (bySeed.TryGetValue(e.ageSeed, out int f)) Join(f, i); else bySeed[e.ageSeed] = i;
            }
            if (map.deckPairs != null)
                foreach (var pr in map.deckPairs) if (pr.union) Join(pr.a, pr.b);
            // the divided roads: by name, then by distance
            var byName = new Dictionary<string, List<int>>();
            var box = new Rect[ne];
            for (int i = 0; i < ne; i++)
            {
                var e = map.edges[i];
                if (!e.oneway || e.link || e.cls < 2 || string.IsNullOrEmpty(e.name) || e.pts == null || e.pts.Length < 2 || e.a == e.b) continue;
                Vector2 mn = e.pts[0], mx = e.pts[0];
                foreach (var q in e.pts) { mn = Vector2.Min(mn, q); mx = Vector2.Max(mx, q); }
                box[i] = Rect.MinMaxRect(mn.x - DividedReachM, mn.y - DividedReachM, mx.x + DividedReachM, mx.y + DividedReachM);
                if (!byName.TryGetValue(e.name, out var l)) byName[e.name] = l = new List<int>();
                l.Add(i);
            }
            foreach (var l in byName.Values)
            {
                if (l.Count < 2) continue;
                foreach (int i in l)
                {
                    var e = map.edges[i];
                    var mid = e.PointAt(e.length * 0.5f);
                    var tg = e.TangentAt(e.length * 0.5f);
                    foreach (int j in l)
                    {
                        if (j == i || Find(j) == Find(i) || !box[j].Contains(mid)) continue;
                        var o = map.edges[j];
                        CityElevation.ProjectOn(o, mid, out float at);
                        if (Vector2.Distance(o.PointAt(at), mid) > DividedReachM) continue;
                        if (Vector2.Dot(o.TangentAt(at), tg) > -0.85f) continue;   // not running the other way
                        Join(i, j);
                    }
                }
            }
            // leftover item 3: a road goes on in one surface through its junctions -
            // the main road's two arms first, then the two arms of a road crossing
            // it under one name - as one resurfacing contract does, a stretch at a
            // time: a join that would make a group longer than FanJoinMaxM is left
            // (that junction's main road changes age at its mouth). Unbounded, the
            // joins ran one surface over most of the city and new asphalt fell from
            // 38% of the road length to 11% (the first audit of this item).
            FanJoins.Clear(); FanJoinsMade = 0;
            if (t != null) FanMainArms(map, t, FanJoins);
            if (FanSurfaceOn)
            {
                var len = new float[ne];
                for (int i = 0; i < ne; i++) len[Find(i)] += map.edges[i].length;
                for (int pass = 0; pass < 2; pass++)
                    foreach (var (ja, jb, main) in FanJoins)
                    {
                        if (main != (pass == 0)) continue;
                        int ra = Find(ja), rb = Find(jb);
                        if (ra == rb || len[ra] + len[rb] > FanJoinMaxM) continue;
                        float sum = len[ra] + len[rb];
                        Join(ra, rb);
                        len[Find(ra)] = sum;
                        FanJoinsMade++;
                    }
            }
            // each group's seed: the lowest (x, then z) of its members'
            var seedOf = new Dictionary<int, Vector2>();
            for (int i = 0; i < ne; i++)
            {
                var e = map.edges[i];
                if (!e.hasAgeSeed) continue;
                int r = Find(i);
                if (!seedOf.TryGetValue(r, out var sd) || e.ageSeed.x < sd.x || (e.ageSeed.x == sd.x && e.ageSeed.y < sd.y)) seedOf[r] = e.ageSeed;
            }
            int shared = 0;
            for (int i = 0; i < ne; i++)
            {
                var e = map.edges[i];
                if (!e.hasAgeSeed) continue;
                var sd = seedOf[Find(i)];
                if (sd != e.ageSeed) { e.ageSeed = sd; shared++; }
            }
            SharedAgeEdges = shared;
        }

        /// <summary>How far apart a divided road's two carriageways may be and
        /// still share one surface age (the elevation solve's PairReachM).</summary>
        const float DividedReachM = 40f;
        /// <summary>Edges whose surface age the last ShareSurfaceAges moved.</summary>
        public static int SharedAgeEdges { get; private set; }

        // ==================================================================
        /// <summary>Build a tile in one go: every step of <see cref="Begin"/>'s
        /// job, run to the end (the audits, the probes, the grid before a
        /// race's countdown, and the tile under a car that cannot wait).</summary>
        public static TileMeshes Build(CityMap map, Trims trims,
            Dictionary<long, List<CityBuildings.B>> buildings, int tx, int tz)
        {
            var job = Begin(map, trims, buildings, tx, tz);
            job.Finish();
            return job.Result;
        }
        /// <summary>Inside a tile build: LaneExtents computes (the section cache is being filled).</summary>
        static bool inBuild;

        // ------------------------------------------------------------------
        //  THE TIME-SLICED TILE BUILD (WP-09, owner 2026-10-01: "It regularly
        //  bogs down about every two blocks" in Uptown). A tile was built in
        //  one frame: 47-117 ms typical and up to 230-350 ms for one Uptown
        //  tile in the editor, ~70% of it laying roads and decks, and five of
        //  them in a row every time the car crossed a tile line. A TileJob is
        //  the same build as a sequence of small steps - a road segment, a
        //  gore, a junction, a footprint, four rows of ground, one mesh -
        //  that CityWorld runs a few milliseconds a frame.
        //
        //  The steps are the one-go build's own code in its own order, so a
        //  sliced tile is the same tile bit for bit (CityBudgetProbe checks).
        //  The builder's scratch (buckets, caches, the lattice) is static and
        //  belongs to ONE build: Begin finishes any job still running before
        //  it starts another, and CityWorld runs no tree or sign frame - which
        //  swap the lattice in and out - while a job is open.
        // ------------------------------------------------------------------
        static TileJob activeJob;
        /// <summary>WP-09: section every road the tile will lay, and each
        /// one's squeezed neighbours, a step each BEFORE they are needed, so a
        /// span or a gore never pays for a long neighbour's first sectioning
        /// inside its own step. The sections are the same either way (each
        /// edge's are its own, cached by edge; CityBudgetProbe compares the
        /// tiles against the build with this off).</summary>
        public static bool WarmSections = true;
        /// <summary>What the step under way is building (the probe's labels):
        /// the phase (<see cref="PhaseNames"/>), the item and a part of it.</summary>
        static int stepPhase, stepItem, stepPart;
        /// <summary>The job whose steps own the builder's scratch, or null.</summary>
        public static TileJob ActiveJob => activeJob;
        static readonly System.Diagnostics.Stopwatch phaseClock = new System.Diagnostics.Stopwatch();

        /// <summary>Start building a tile in steps (<see cref="TileJob.Step"/>).
        /// A job still running is finished first.</summary>
        public static TileJob Begin(CityMap map, Trims trims,
            Dictionary<long, List<CityBuildings.B>> buildings, int tx, int tz)
        {
            if (activeJob != null) activeJob.Finish();
            var tm = new TileMeshes { origin = new Vector3(tx * TileSize, 0f, tz * TileSize), tap = RecordTap ? new RoadTap() : null };
            var job = new TileJob(tx, tz, tm, TileSteps(map, trims, buildings, tx, tz, tm).GetEnumerator());
            activeJob = job;
            return job;
        }

        public sealed class TileJob
        {
            public readonly int tx, tz;
            public readonly TileMeshes Result;
            IEnumerator<int> steps;
            /// <summary>Every step has run; <see cref="Result"/> is complete.</summary>
            public bool Done { get; private set; }
            /// <summary>Work done so far, ms (no idle time between frames).</summary>
            public double WorkMs { get; private set; }
            /// <summary>The longest single step, and the longest one call to
            /// <see cref="Step"/> (a frame's share), ms.</summary>
            public double MaxStepMs { get; private set; }
            public double MaxSliceMs { get; private set; }
            public int Steps { get; private set; }
            public int Slices { get; private set; }
            /// <summary>Set it to a list to record every step's ms (the probe).</summary>
            public List<float> StepLog;
            /// <summary>Set it to a list to record every step over
            /// <see cref="BigStepMs"/>: its phase, item (an edge, a node, a
            /// footprint) and part, and how long it took (the probe).</summary>
            public List<(int phase, int item, int part, float ms, float missMs)> BigSteps;
            public float BigStepMs = 8f;

            internal TileJob(int tx, int tz, TileMeshes tm, IEnumerator<int> steps)
            { this.tx = tx; this.tz = tz; Result = tm; this.steps = steps; }

            /// <summary>Run steps until <paramref name="budgetMs"/> is spent
            /// (at least one). True when the tile is done.</summary>
            public bool Step(double budgetMs)
            {
                if (Done) return true;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                double last = 0.0;
                inBuild = true;
                phaseClock.Start();
                try
                {
                    while (true)
                    {
                        long miss0 = sectionMissTicks;
                        bool more = steps.MoveNext();
                        double now = clock.Elapsed.TotalMilliseconds;
                        Steps++;
                        StepLog?.Add((float)(now - last));
                        if (BigSteps != null && now - last > BigStepMs)
                            BigSteps.Add((stepPhase, stepItem, stepPart, (float)(now - last),
                                          (float)((sectionMissTicks - miss0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency)));
                        if (now - last > MaxStepMs) MaxStepMs = now - last;
                        last = now;
                        if (!more) { Close(); break; }
                        if (now >= budgetMs) break;
                    }
                }
                catch { Close(); throw; }
                finally
                {
                    inBuild = false;
                    phaseClock.Stop();
                    double ms = clock.Elapsed.TotalMilliseconds;
                    WorkMs += ms; Slices++;
                    if (ms > MaxSliceMs) MaxSliceMs = ms;
                }
                return Done;
            }

            /// <summary>Run every step left, now.</summary>
            public void Finish() { while (!Step(double.MaxValue)) { } }

            /// <summary>Give the job up (the next build clears its scratch).</summary>
            public void Abandon() { Close(); }

            void Close()
            {
                Done = true;
                steps?.Dispose();
                steps = null;
                if (activeJob == this) activeJob = null;
            }
        }

        static IEnumerable<int> TileSteps(CityMap map, Trims trims,
            Dictionary<long, List<CityBuildings.B>> buildings, int tx, int tz, TileMeshes tm)
        {
            var min = new Vector2(tx * TileSize, tz * TileSize);
            var max = min + new Vector2(TileSize, TileSize);

            foreach (var b in buckets) b.Clear();
            barrierBucket.Clear();
            guardBucket.Clear();
            kerbBucket.Clear();
            lampBucket.Clear();
            bankBucket.Clear();
            lampBuildings = buildings;
            goreGaps.Clear();
            goreQuads.Clear(); goreGroups.Clear();
            clips.Clear();
            latticeCache.Clear();
            LatticeWindow(min);
            CityHouses.Bind(map, trims, buildings);   // leftover item 6: the houses' lots and driveways
            pavedCache.Clear();
            fanStructure.Clear();
            fanPolys.Clear(); outlines.Clear(); ClearSectionCaches(); pavementVersion = -1;
            tileOrigin = tm.origin;

            phaseClock.Restart();
            PrepareFanFloor(map, trims, min, max);   // roads pass L7: the lattice under the fans
            foreach (var _ in BuildGround(map, tm, min)) yield return 0;
            Phase(0);
            clipPairs.Clear(); goreEdges.Clear();
            foreach (var _ in BuildGores(map, trims, tm, min, max)) yield return 0;
            Phase(1);
            // A gore's nose solves its verge before every branch's clip is in
            // the table: the outlines it sectioned are sectioned again with all
            // of them, and the fans it read are cornered again too (an arm's
            // clip and squeeze decide whether it has a mouth at all).
            outlines.Clear(); ClearSectionCaches(); fanPolys.Clear(); pavementVersion = -1;
            foreach (var _ in BuildRoadsAndDecks(map, trims, tm, min, max)) yield return 0;
            Phase(2);
            foreach (var _ in BuildJunctions(map, trims, tm, min, max)) yield return 0;
            Phase(3);
            stepPhase = 4; stepItem = -1; stepPart = 0;
            BuildWater(map, tm, min, max);
            Phase(4);
            yield return 0;
            stepPhase = 5; stepItem = -1; stepPart = 0;
            BuildCulverts(map, trims, tm, min, max);
            Phase(5);
            yield return 0;
            stepPhase = 6; stepItem = -1; stepPart = 0;
            BuildBuildings(map, buildings, tm, tx, tz);
            Phase(6);
            yield return 0;
            foreach (var _ in BuildFootprints(map, trims, tm, tx, tz)) yield return 0;
            Phase(7);
            stepPhase = 8; stepItem = -1; stepPart = 0;
            BuildHouses(map, tm, tx, tz);
            Phase(8);
            yield return 0;

            stepPhase = 9; stepItem = 0; stepPart = 0;
            FlushLotIslands(map, tm, min);   // leftover item 4: the lots' islands, tops and curbs
            tm.ground = MeshFrom("ground", new[] { Slot.Ground, Slot.Pavement }, out var gSlots, false);
            tm.groundSlots = gSlots;
            yield return 0;
            FinishMesh(tm.ground);
            yield return 0;
            stepItem = 1;
            tm.roads = MeshFrom("roads", RoadAndStructureSlots, out var roadSlots, false);
            tm.roadSlots = roadSlots;
            if (tm.tap != null) tm.tap.slotBase = tapSlotBase;
            yield return 0;
            FinishMesh(tm.roads);
            yield return 0;
            tm.barriers = MeshFromBucket("barriers", barrierBucket);
            tm.guardrails = MeshFromBucket("guardrails", guardBucket);
            tm.kerbs = MeshFromBucket("kerbs", kerbBucket);
            tm.lampPosts = MeshFromBucket("lamps", lampBucket);
            tm.banks = MeshFromBucket("banks", bankBucket);
            tm.water = MeshFrom("water", new[] { Slot.Water }, out _);
            yield return 0;
            stepItem = 3;
            tm.buildings = MeshFrom("bld", BuildingSlots, out var bSlots, false);
            tm.buildingSlots = bSlots;
            yield return 0;
            stepItem = 4;
            FinishMesh(tm.buildings);
            Phase(9);
        }

        // ---- where a tile build's time goes (WP-09 instrument) -------------
        /// <summary>The build phases, in order: what CityBudgetProbe reports
        /// per site, so the time-sliced build pauses where the time is.</summary>
        public static readonly string[] PhaseNames =
            { "ground", "gores", "roads", "junctions", "water", "culverts", "buildings", "footprints", "houses", "meshes" };
        /// <summary>Milliseconds per phase, summed over every build since
        /// <see cref="ResetPhaseClock"/>.</summary>
        public static readonly double[] PhaseMs = new double[10];
        /// <summary>The longest one phase took in any one tile since the reset.</summary>
        public static readonly double[] PhaseMaxMs = new double[10];
        public static void ResetPhaseClock()
        {
            System.Array.Clear(PhaseMs, 0, PhaseMs.Length);
            System.Array.Clear(PhaseMaxMs, 0, PhaseMaxMs.Length);
        }
        static void Phase(int i)
        {
            double ms = phaseClock.Elapsed.TotalMilliseconds;
            PhaseMs[i] += ms;
            if (ms > PhaseMaxMs[i]) PhaseMaxMs[i] = ms;
            phaseClock.Restart();
        }

        static readonly Slot[] BuildingSlots =
        {
            Slot.FacadeTower, Slot.FacadeMid, Slot.FacadeBrick, Slot.Shops,
            Slot.FacadeGlass, Slot.FacadeHouse, Slot.RoofTiles, Slot.RoofFlat,
        };

        /// <summary>Every driving surface plus the structural concrete, in slot
        /// order. MeshFrom drops the empty ones, so a tile with two profiles on
        /// it still ends up with two submeshes and not eighty.</summary>
        static readonly Slot[] RoadAndStructureSlots = BuildRoadSlotList();
        static Slot[] BuildRoadSlotList()
        {
            var list = new Slot[RoadClassCount * SurfaceCount + 1];
            for (int i = 0; i < list.Length - 1; i++) list[i] = (Slot)((int)Slot.RoadFirst + i);
            list[list.Length - 1] = Slot.Concrete;
            return list;
        }

        static Mesh MeshFrom(string name, Slot[] wanted, out Slot[] usedSlots) =>
            MeshFrom(name, wanted, out usedSlots, true);

        /// <summary>WP-09: <paramref name="finish"/> false leaves the normals
        /// and bounds to <see cref="FinishMesh"/>, a step of their own.</summary>
        static void FinishMesh(Mesh m)
        {
            if (m == null) return;
            m.RecalculateNormals();
            m.RecalculateBounds();
        }

        static Mesh MeshFrom(string name, Slot[] wanted, out Slot[] usedSlots, bool finish)
        {
            int totalV = 0;
            var used = new List<Slot>();
            foreach (var s in wanted)
                if (buckets[(int)s].Count > 0) { used.Add(s); totalV += buckets[(int)s].Count; }
            usedSlots = used.ToArray();
            if (totalV == 0) return null;

            var mesh = new Mesh { name = name };
            if (totalV > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var verts = new List<Vector3>(totalV);
            var uvs = new List<Vector2>(totalV);
            bool anyCol = false;
            foreach (var s in used) { verts.AddRange(buckets[(int)s].v); uvs.AddRange(buckets[(int)s].uv); anyCol |= buckets[(int)s].col != null; }
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            if (anyCol)
            {
                // the facade atlas's tint + column (Uptown B1); every other
                // slot's material never reads the colour
                var cols = new List<Color32>(totalV);
                var white = new Color32(128, 128, 128, 0);
                foreach (var s in used)
                {
                    var bk = buckets[(int)s];
                    if (bk.col != null && bk.col.Count == bk.Count) cols.AddRange(bk.col);
                    else for (int k = 0; k < bk.Count; k++) cols.Add(white);
                }
                mesh.SetColors(cols);
            }
            mesh.subMeshCount = used.Count;
            int baseV = 0;
            if (RecordTap) tapSlotBase = new int[used.Count];
            for (int i = 0; i < used.Count; i++)
            {
                var bk = buckets[(int)used[i]];
                if (RecordTap) tapSlotBase[i] = baseV;
                var tris = new int[bk.t.Count];
                for (int j = 0; j < tris.Length; j++) tris[j] = bk.t[j] + baseV;
                mesh.SetTriangles(tris, i, false);
                baseV += bk.Count;
            }
            if (finish) FinishMesh(mesh);
            return mesh;
        }

        static Mesh MeshFromBucket(string name, Bucket bk)
        {
            if (bk.Count == 0) return null;
            var mesh = new Mesh { name = name };
            if (bk.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(bk.v);
            mesh.SetUVs(0, bk.uv);
            mesh.SetTriangles(bk.t, 0, false);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// The ground, one quad per 8 m cell, grass or PAVEMENT. A downtown is
        /// paved edge to edge, and the first preview stood the skyline on a
        /// lawn: a cell within a kilometre of Trade & Tryon, or within 20 m of
        /// any real building that is not a house, is concrete. Per-cell quads
        /// rather than a shared lattice so a cell can change bucket; four
        /// times the ground vertices of a tile, which is still nothing.
        /// </summary>
        static IEnumerable<int> BuildGround(CityMap map, TileMeshes tm, Vector2 min)
        {
            int res = GroundRes;
            float cell = TileSize / res;
            int stride = res + 1;
            int ix0 = Mathf.RoundToInt(min.x / cell), iz0 = Mathf.RoundToInt(min.y / cell);
            var heights = new float[stride * stride];
            for (int z = 0; z <= res; z++)
                for (int x = 0; x <= res; x++)
                    heights[z * stride + x] = LatticeVertex(map, ix0 + x, iz0 + z);
            // roads pass L8: the parking lots laid into the lattice
            PrepareTileLots(map, min, min + new Vector2(TileSize, TileSize));
            // leftover item 6: and the houses' driveways
            PrepareTileDrives(map, min, min + new Vector2(TileSize, TileSize));

            for (int z = 0; z < res; z++)
            {
                if ((z & 3) == 0) { yield return 0; stepPhase = 0; stepItem = z; stepPart = 0; }   // WP-09: four rows a step
                for (int x = 0; x < res; x++)
                {
                    bool paved = PavedCell(map, ix0 + x, iz0 + z);
                    var bk = GroundBucket(paved);
                    Vector3 P(int dx, int dz) => new Vector3((x + dx) * cell, heights[(z + dz) * stride + x + dx], (z + dz) * cell);
                    Vector2 U(int dx, int dz) => GroundUV(paved, min.x + (x + dx) * cell, min.y + (z + dz) * cell);
                    if (LotCell(map, tm, P(0, 0), P(0, 1), P(1, 1), P(1, 0), paved)) continue;
                    if (DrivewayCell(tm, P(0, 0), P(0, 1), P(1, 1), P(1, 0), paved)) continue;
                    bk.Up(P(0, 0), P(0, 1), P(1, 1), P(1, 0), U(0, 0), U(0, 1), U(1, 1), U(1, 0));
                }
            }
        }

        static Bucket GroundBucket(bool paved) => buckets[(int)(paved ? Slot.Pavement : Slot.Ground)];
        /// <summary>World-planar, so a verge and the cell it tucks under show
        /// the same texels where they cross.</summary>
        static Vector2 GroundUV(bool paved, float wx, float wz)
        {
            float tile = paved ? 6f : 24f;
            return new Vector2(wx / tile, wz / tile);
        }

        // ------------------------------------------------------------------
        //  The lattice as the car meets it. Every verge, rail warrant and
        //  retaining face below is solved against THESE triangles — not the
        //  DEM, not GroundY at the point — because the finished lattice is
        //  the surface a verge has to cross.
        // ------------------------------------------------------------------
        const float LatticeCell = TileSize / GroundRes;
        static Dictionary<long, float> latticeCache = new Dictionary<long, float>(4096);
        /// <summary>WP-08: hand over the lattice the last tile build cached (a
        /// fresh one takes its place), and put one back - so the trees, planted
        /// on a later frame, stand on their tile's lattice without recomputing
        /// GroundY at every corner.</summary>
        public static Dictionary<long, float> TakeLattice() { var d = latticeCache; latticeCache = new Dictionary<long, float>(4096); latGen++; return d; }
        public static void PutLattice(Dictionary<long, float> d) { if (d != null) latticeCache = d; latGen++; }
        static readonly Dictionary<long, bool> pavedCache = new Dictionary<long, bool>(1024);
        static long LatticeKey(int ix, int iz) => ((long)ix << 32) ^ (uint)iz;

        /// <summary>GroundY at a lattice corner, by global index: ix * 8 is
        /// exactly the float every tile computes for that corner, so the
        /// neighbouring tile's corner is the same number.</summary>
        static float LatticeVertex(CityMap map, int ix, int iz)
        {
            // roads pass L8: an array in front of the dictionary for the tile
            // being built and 128 m round it (half a million reads a ring;
            // the dictionary stays the record, the trees borrow it)
            int wx = ix - latWinX0, wz = iz - latWinZ0;
            int wi = (uint)wx < LatWinN && (uint)wz < LatWinN ? wz * LatWinN + wx : -1;
            if (wi >= 0 && latWinGen[wi] == latGen) return latWinVal[wi];
            long k = LatticeKey(ix, iz);
            if (!latticeCache.TryGetValue(k, out float y))
            {
                // GroundY - with (leftover item 6) a house's lot graded level under it
                y = CityHouses.Lattice(map, ix, iz);
                if (fanFloors.Count > 0) y = FanFloor(ix * LatticeCell, iz * LatticeCell, y);
                latticeCache[k] = y;
            }
            if (wi >= 0) { latWinVal[wi] = y; latWinGen[wi] = latGen; }
            return y;
        }
        const int LatWinMargin = 16, LatWinN = GroundRes + 1 + 2 * LatWinMargin;
        static readonly float[] latWinVal = new float[LatWinN * LatWinN];
        static readonly int[] latWinGen = new int[LatWinN * LatWinN];
        static int latGen = 1, latWinX0 = int.MinValue / 2, latWinZ0 = int.MinValue / 2;
        /// <summary>The array window follows the dictionary: any change of
        /// dictionary (a new tile, the trees' swap) empties it.</summary>
        static void LatticeWindow(Vector2 min)
        {
            latGen++;
            latWinX0 = Mathf.RoundToInt(min.x / LatticeCell) - LatWinMargin;
            latWinZ0 = Mathf.RoundToInt(min.y / LatticeCell) - LatWinMargin;
        }

        /// <summary>For the roadside probe: <see cref="LatticeY"/>, to tell the
        /// lattice from a verge strip where land stands over a lane.</summary>
        public static float LatticeAt(CityMap map, float x, float z) => LatticeY(map, x, z);

        /// <summary>The ground mesh's height at a plan point: the triangle
        /// BuildGround drew there. Bucket.Up draws every cell's quad with its
        /// diagonal from (0,0) to (1,1). Reads the tile build's cache.</summary>
        static float LatticeY(CityMap map, float x, float z)
        {
            float fx = x / LatticeCell, fz = z / LatticeCell;
            int ix = Mathf.FloorToInt(fx), iz = Mathf.FloorToInt(fz);
            float tx = fx - ix, tz = fz - iz;
            float h00 = LatticeVertex(map, ix, iz), h11 = LatticeVertex(map, ix + 1, iz + 1);
            if (tx >= tz)
            {
                float h10 = LatticeVertex(map, ix + 1, iz);
                return h00 + tx * (h10 - h00) + tz * (h11 - h10);
            }
            float h01 = LatticeVertex(map, ix, iz + 1);
            return h00 + tz * (h01 - h00) + tx * (h11 - h01);
        }

        /// <summary>
        /// Is the lattice cell with this corner paved? A downtown is paved
        /// edge to edge, and the first preview stood the skyline on a lawn: a
        /// cell within a kilometre of Trade & Tryon, or within 20 m of any
        /// real building that is not a house, is concrete.
        /// </summary>
        static bool PavedCell(CityMap map, int ix, int iz)
        {
            long k = LatticeKey(ix, iz);
            if (pavedCache.TryGetValue(k, out bool paved)) return paved;
            var c = new Vector2((ix + 0.5f) * LatticeCell, (iz + 0.5f) * LatticeCell);
            paved = Vector2.Distance(c, map.uptown) < 1000f || map.AnyFootprintNear(c, 20f, nonHouseOnly: true);
            pavedCache[k] = paved;
            return paved;
        }

        static bool PavedAt(CityMap map, float x, float z) =>
            PavedCell(map, Mathf.FloorToInt(x / LatticeCell), Mathf.FloorToInt(z / LatticeCell)) || ApronAt(map, x, z);

        // ------------------------------------------------------------------
        //  Branches: a ramp beside its mainline, the minor arm of a fork.
        //
        //  OSM joins the ramp to the carriageway at the END of the taper; the
        //  ramp's own geometry then diverges at a shallow angle, and for its
        //  first hundred metres its ribbon lies INSIDE the mainline's. So the
        //  branch is CLIPPED: while its inner edge is inside the host it is
        //  moved out to the host's edge, and while the whole ribbon is
        //  inside, both edges collapse onto it — the ribbon starts as a
        //  zero-width wedge at the host's edge and widens as it leaves. The
        //  painted gore (the wedge between the two pavements once they part)
        //  is filled for as long as they run within a few metres. The branch's
        //  inner side stands down for the whole attachment; the host's side
        //  only where the branch is not collapsed (see EmitBranch), and a gore
        //  nose on a deck is closed by a rail block (EmitNose).
        //
        //  Both roads are CHAINS walked away from the node: a mainline is cut
        //  at every ramp node, so a ramp that runs alongside for longer than
        //  the piece it joins is clipped against the next piece too; and the
        //  ramp itself is cut at its own nodes, so its earlier pieces are
        //  clipped as well. A chain is ONE polyline, projected onto as a
        //  whole — projecting onto the pieces one at a time and taking the
        //  nearest clamped its way onto the end of a three-metre sliver and
        //  hung the ramp's inner edge off a phantom line.
        // ------------------------------------------------------------------
        const float GoreStep = 6f;
        const float GoreReach = 420f;
        const float GoreMaxGap = 4.5f;
        /// <summary>How far the painted gore reaches under the pavement of the
        /// two roads either side of it (see EmitBranch).</summary>
        const float GoreSeamM = 0.3f;
        /// <summary>The narrowest gap between a branch and its host that is
        /// painted: a quad is drawn as long as the two pavements do not
        /// overlap by more than this, so the seam under a gap of a couple of
        /// centimetres is floored too. It was 2 cm the other way, and a slot
        /// five centimetres wide along a ramp beside I-277 had nothing in it.</summary>
        const float GoreMinGapM = -0.25f;
        /// <summary>Width of the flush strip laid under a ribbon side that
        /// stands down for another surface (see EmitSide).</summary>
        const float GapSeamM = 0.6f;
        /// <summary>Width of that strip where the side is on a deck: the
        /// crack between two decks is a few centimetres, and nothing beside a
        /// deck hides a strip that runs out past the other surface.</summary>
        const float DeckSeamM = 0.3f;
        /// <summary>How far that strip falls across its width.</summary>
        const float SeamFallM = 0.08f;
        /// <summary>How far under the surface above it a seam strip runs once
        /// past the crack (plan L3 / A3 FD8: an underlay at least 8 cm down
        /// does not flicker), and how soon it gets there.</summary>
        const float SeamUnderM = 0.08f, SeamDropRunM = 0.06f;
        /// <summary>How far a squeeze half strip looks for the pavement beside
        /// it (twice the widest strip a squeeze leaves at a section), and how
        /// far past the middle of the gap it reaches, under the other half.</summary>
        const float HalfGapReachM = 2.4f, HalfGapOverlapM = 0.1f;
        /// <summary>How far a squeeze half strip runs on under a pavement
        /// no lower than itself, past that pavement's drawn edge. The strip
        /// is straight between cross-sections VergeStepM apart and the edge
        /// beside it bends at its own sections, so a strip reaching only to
        /// the edge left a slot up to 12 cm wide at the foot of a retaining
        /// face (North Caldwell Street's e5507 beside e16681).</summary>
        const float HalfGapUnderM = 0.15f;
        const float ClipLift = 0f;      // a clipped vertex sits ON the host's edge (a 3 cm slot swallowed a wheel probe)
        /// <summary>A branch further than this above or below its host is
        /// climbing away from it, not running beside it, whatever the plan
        /// view says. Clipping it there hung slivers of ramp in the air.</summary>
        const float AttachDy = 0.6f;

        /// <summary>Barrier / rail gaps: (edge, side, s0, s1). Side is the
        /// ribbon's own: -1 the L vertex (p - right*hw), +1 the R vertex.</summary>
        static readonly List<(int edge, int side, float s0, float s1)> goreGaps = new List<(int, int, float, float)>();

        /// <summary>A road as one polyline: an edge and its through-
        /// continuations, walked away from a node.</summary>
        internal class Chain
        {
            public readonly List<Vector2> pts = new List<Vector2>(64);
            public readonly List<CityMap.Edge> seg = new List<CityMap.Edge>(64);   // per segment
            public readonly List<float> sA = new List<float>(64), sB = new List<float>(64);   // arc on seg at its ends
            public readonly List<CityMap.Edge> edges = new List<CityMap.Edge>(4);
            public readonly List<float> cum = new List<float>(64);   // cumulative length at each pt

            public float Length => cum.Count > 0 ? cum[cum.Count - 1] : 0f;

            /// <summary>Nearest point on the chain: the edge and arc it lies
            /// on, and the chain's own walking direction there.</summary>
            public void Project(Vector2 p, out CityMap.Edge H, out float sM, out Vector2 q, out Vector2 dir)
                => Project(p, out H, out sM, out q, out dir, out _);

            /// <summary><paramref name="atEnd"/>: the nearest point is the
            /// chain's LAST vertex — the road being projected on has run out
            /// and the distance measured there is to a corner, not to a side.</summary>
            public void Project(Vector2 p, out CityMap.Edge H, out float sM, out Vector2 q, out Vector2 dir, out bool atEnd)
                => Project(p, out H, out sM, out q, out dir, out atEnd, out _);

            /// <summary><paramref name="arc"/>: the projection's distance along
            /// the CHAIN from its node — the one coordinate that does not
            /// restart at every piece boundary.</summary>
            public void Project(Vector2 p, out CityMap.Edge H, out float sM, out Vector2 q, out Vector2 dir, out bool atEnd, out float arc)
            {
                float best = float.MaxValue; int bi = 0; float bt = 0f;
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    Vector2 a = pts[i], d = pts[i + 1] - a;
                    float L2 = d.sqrMagnitude;
                    float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                    float dd = (p - (a + d * t)).sqrMagnitude;
                    if (dd < best) { best = dd; bi = i; bt = t; }
                }
                H = seg[bi];
                sM = Mathf.Lerp(sA[bi], sB[bi], bt);
                q = pts[bi] + (pts[bi + 1] - pts[bi]) * bt;
                dir = (pts[bi + 1] - pts[bi]).normalized;
                atEnd = bi == pts.Count - 2 && bt > 0.999f;
                arc = Mathf.Lerp(cum[bi], cum[bi + 1], bt);
            }

            /// <summary>The chain-arc range one of its pieces covers.</summary>
            public bool PieceRange(CityMap.Edge piece, out float c0, out float c1)
            {
                c0 = float.MaxValue; c1 = float.MinValue;
                for (int i = 0; i < seg.Count; i++)
                {
                    if (seg[i] != piece) continue;
                    c0 = Mathf.Min(c0, cum[i]);
                    c1 = Mathf.Max(c1, cum[i + 1]);
                }
                return c1 >= c0;
            }

            /// <summary>The point at an arc distance along the chain.</summary>
            public bool Walk(float dist, out CityMap.Edge E, out float sE, out Vector2 p, out Vector2 dir)
            {
                E = null; sE = 0f; p = default; dir = Vector2.up;
                if (pts.Count < 2 || dist > Length) return false;
                int i = 0;
                while (i + 2 < pts.Count && cum[i + 1] < dist) i++;
                float segL = cum[i + 1] - cum[i];
                float t = segL > 1e-6f ? Mathf.Clamp01((dist - cum[i]) / segL) : 0f;
                E = seg[i];
                sE = Mathf.Lerp(sA[i], sB[i], t);
                p = pts[i] + (pts[i + 1] - pts[i]) * t;
                dir = (pts[i + 1] - pts[i]).normalized;
                return true;
            }
        }

        /// <summary>Walk a road away from a node through its through-
        /// continuations, out to a reach. A ramp's chain stays on ramps and
        /// never enters the host it is being clipped against.</summary>
        static Chain BuildChain(CityMap map, CityMap.Edge first, int node, bool linkChain, float reach, Chain avoid)
        {
            var ch = new Chain();
            var cur = first; int at = node; float len = 0f;
            for (int k = 0; k < 6; k++)
            {
                ch.edges.Add(cur);
                bool fromA = cur.a == at;
                int n = cur.pts.Length;
                for (int i = 0; i < n; i++)
                {
                    int pi = fromA ? i : n - 1 - i;
                    var p = cur.pts[pi];
                    float sOn = cur.s[pi];
                    if (i == 0)
                    {
                        if (ch.pts.Count == 0) { ch.pts.Add(p); ch.cum.Add(0f); }
                        continue;   // a later piece's first point is the shared node, already there
                    }
                    int prevPi = fromA ? pi - 1 : pi + 1;
                    ch.seg.Add(cur); ch.sA.Add(cur.s[prevPi]); ch.sB.Add(sOn);
                    ch.cum.Add(ch.cum[ch.cum.Count - 1] + Vector2.Distance(ch.pts[ch.pts.Count - 1], p));
                    ch.pts.Add(p);
                }
                len += cur.length;
                if (len >= reach) break;
                int far = fromA ? cur.b : cur.a;
                var nx = NextThrough(map, cur, far, linkChain);
                if (nx == null || ch.edges.Contains(nx)) break;
                if (avoid != null && avoid.edges.Contains(nx)) break;
                if (linkChain && (nx.link != first.link || (!first.link && nx.cls != first.cls))) break;
                cur = nx; at = far;
            }
            return ch;
        }

        /// <summary>The edge continuing <paramref name="e"/> through
        /// <paramref name="node"/>: the arm leaving the node most nearly the
        /// way e arrived. Ramps are skipped for a mainline chain.</summary>
        static CityMap.Edge NextThrough(CityMap map, CityMap.Edge e, int node, bool allowLinks)
        {
            var dIn = -OutDir(e, node);
            CityMap.Edge best = null; float bd = -ThroughCos;
            foreach (var oi in map.nodeEdges[node])
            {
                var o = map.edges[oi];
                if (o == e || o.a == o.b) continue;
                if (o.link && !allowLinks) continue;
                float d = Vector2.Dot(dIn, OutDir(o, node));
                if (d < bd) continue;
                if (best == null || d > bd) { best = o; bd = d; }
            }
            return best;
        }

        /// <summary>One branch edge's attachment to a host chain: the arc
        /// range that is clipped, which side of the host it lies on (in the
        /// host CHAIN's frame) and which of its own vertices faces the host.</summary>
        class Clip
        {
            public Chain host;
            public float sFrom, sTo;
            public int side;        // host-chain frame: +1 = the chain's left (its rM)
            public int innerSide;   // the branch edge's own frame: +1 = its R vertex
        }
        static readonly Dictionary<int, List<Clip>> clips = new Dictionary<int, List<Clip>>();   // per branch edge

        static Clip ClipAt(CityMap.Edge e, float s)
        {
            if (!clips.TryGetValue(e.index, out var list)) return null;
            foreach (var c in list) if (s >= c.sFrom && s <= c.sTo) return c;
            return null;
        }

        static IEnumerable<int> BuildGores(CityMap map, Trims trims, TileMeshes tm, Vector2 min, Vector2 max)
        {
            // Branch ends within reach of the tile: a clip starting outside
            // can run inside, and a barrier gap inside can come from a node outside.
            segScratch.Clear();
            map.EdgeSegsInRect(min - Vector2.one * (GoreReach + 20f), max + Vector2.one * (GoreReach + 20f), segScratch);
            edgeScratch.Clear();
            foreach (var packed in segScratch) edgeScratch.Add(packed >> 12);
            foreach (var ei in edgeScratch)
            {
                yield return 0;   // WP-09: a branch edge a step
                stepPhase = 1; stepItem = ei; stepPart = 0;
                var L = map.edges[ei];
                for (int end = 0; end < 2; end++)
                {
                    int n = end == 0 ? L.a : L.b;
                    int hostIdx = end == 0 ? trims.branchA[ei] : trims.branchB[ei];
                    if (hostIdx < 0) continue;
                    var np = map.nodes[n];
                    if (np.x < min.x - GoreReach - 20f || np.x > max.x + GoreReach + 20f ||
                        np.y < min.y - GoreReach - 20f || np.y > max.y + GoreReach + 20f) continue;
                    stepPart = 20;
                    foreach (var _ in EmitBranch(map, trims, tm, min, max, L, map.edges[hostIdx], n)) yield return 0;
                }
            }
        }

        /// <summary>The host's half width on the branch's side, in the host
        /// CHAIN's frame: the host edge's squeezed extent, so a clipped
        /// vertex lands on the edge the host actually draws.</summary>
        static float HostHalf(CityMap map, Trims trims, CityMap.Edge H, float sM, Vector2 rMChain, int side)
        {
            LaneExtents(map, trims, H, sM, out float hl, out float hr);
            var tH = H.TangentAt(sM);
            bool same = Vector2.Dot(rMChain, new Vector2(-tH.y, tH.x)) >= 0f;
            int sideH = same ? side : -side;
            return sideH > 0 ? hr : hl;
        }

        /// <summary>The point on an edge's drawn pavement edge (the line
        /// model's, unsqueezed) on the side <paramref name="sign"/> of a
        /// lateral direction <paramref name="r"/> (a chain's own left, which
        /// may face either way along the edge), from its OSM point p.</summary>
        static Vector2 EdgePoint(CityMap.Edge E, float sE, Vector2 p, Vector2 r, float sign) =>
            p + r * (sign > 0f ? ExtToward(E, sE, r, 1f) : -ExtToward(E, sE, r, -1f));

        /// <summary>How far an edge's pavement reaches off its OSM line toward
        /// sign * r.</summary>
        static float ExtToward(CityMap.Edge E, float sE, Vector2 r, float sign)
        {
            LineModel.Extents(E, sE, out float eM, out float eP);
            var t = E.TangentAt(sE);
            bool same = Vector2.Dot(r, new Vector2(-t.y, t.x)) >= 0f;
            return (sign > 0f) == same ? eP : eM;
        }

        /// <summary>One sample of a branch beside its host, as EmitBranch and
        /// its refinement read it.</summary>
        struct BranchSample
        {
            public CityMap.Edge E, H;
            public float sE, sM, hostArc, gap, yIn, yOut, lSign, lHalf;
            /// <summary>The host-chain arc of the branch's OUTER vertex, not
            /// its centreline. Where the branch's outer edge crosses the
            /// host's, this is the arc of the crossing itself; the centreline's
            /// projection lies up to half the branch's width times the sine of
            /// the angle between them further along (see RefineCollapse).</summary>
            public float outerArc;
            public Vector2 p, inner, outer, dirM, rM, rL;
            public bool attached;
            /// <summary>The whole branch ribbon is inside the host here (its
            /// OUTER edge within 15 cm of the host's edge): drawn as a zero-
            /// width wedge, with no surface of its own beyond the host.</summary>
            public bool collapsed;
            public int sideNow;
        }

        static bool SampleBranch(CityMap map, Trims trims, Chain host, Chain br, float travelled, ref int side, out BranchSample o)
        {
            o = default;
            if (!br.Walk(travelled, out var E, out float sE, out var p, out var dirL)) return false;
            host.Project(p, out var H, out float sM, out var q, out var dirM, out bool hostOut, out float arc);
            if (hostOut) return false;   // the host chain ran out before the branch let go of it
            var rM = new Vector2(-dirM.y, dirM.x);
            float off = Vector2.Dot(p - q, rM);
            int sideNow = off >= 0f ? 1 : -1;
            // Which side of the host the branch lies on is read from the
            // first sample that is clearly off the host's centreline. OSM's
            // first segment at the node is often a metre of noise: read
            // from there, a wrong side collapsed the whole ramp onto the
            // FAR edge of its mainline.
            if (side == 0 && Mathf.Abs(off) > 0.4f) side = sideNow;
            var rL = new Vector2(-dirL.y, dirL.x);
            float mHalf = side == 0 ? trims.HalfWidthAt(H, sM) : HostHalf(map, trims, H, sM, rM, side);
            var outer = q + rM * (side * mHalf);
            float lSign = Vector2.Dot(rL, rM) >= 0f ? -side : side;
            float lHalf = ExtToward(E, sE, rL, lSign);
            var inner = EdgePoint(E, sE, p, rL, lSign);
            float gap = side == 0 ? 0f : Vector2.Dot(inner - outer, rM) * side;
            // CutAgainstHost's collapse test, read along the host's normal:
            // its 15 cm is measured along our cross-line, which meets the
            // host's normal at |cos|.
            var outerV = EdgePoint(E, sE, p, rL, -lSign);
            float outerGap = side == 0 ? 0f : Vector2.Dot(outerV - outer, rM) * side;
            float cross = Mathf.Abs(Vector2.Dot(rL, rM));
            host.Project(outerV, out _, out _, out _, out _, out _, out float outerArc);
            o = new BranchSample
            {
                E = E, H = H, sE = sE, sM = sM, hostArc = arc, outerArc = outerArc, gap = gap,
                yIn = E.YAt(sE), yOut = H.YAt(sM), lSign = lSign, lHalf = lHalf,
                p = p, inner = inner, outer = outer, dirM = dirM, rM = rM, rL = rL, sideNow = sideNow,
                collapsed = side == 0 || outerGap <= 0.15f * cross,
            };
            o.attached = (gap <= GoreMaxGap && sideNow == side || Mathf.Abs(off) < 0.4f)
                         && Mathf.Abs(o.yIn - o.yOut) < AttachDy;
            return true;
        }

        /// <summary>Where, between two samples that disagree on
        /// <see cref="BranchSample.collapsed"/>, the branch's outer edge
        /// crosses the host's: the host-chain arc there, to a few decimetres.
        ///
        /// The arc is the one the OUTER VERTEX projects to. It was the
        /// centreline's, and a ramp leaving its host at fifteen or thirty
        /// degrees has its centreline half a ramp's width across the cross-
        /// line from its outer edge: the centreline projected a metre or more
        /// further along the host than the crossing did, so the host's rail
        /// stood on for that metre over the ramp's first sliver of surface —
        /// the drive audit found the rail's top a metre up in the ramp's lane
        /// at I-277/I-77 and John Belk (e8500, e252, e268; a Jersey barrier in
        /// e315's). Measured at the outer vertex, the host's barrier ends where
        /// the ramp's outer barrier begins, on one line.</summary>
        static float RefineCollapse(CityMap map, Trims trims, Chain host, Chain br, float t0, float t1, int side, bool collapsedAt0)
        {
            float arc1 = -1f;
            for (int it = 0; it < 5; it++)
            {
                float tMid = (t0 + t1) * 0.5f;
                int s = side;
                if (!SampleBranch(map, trims, host, br, tMid, ref s, out var m)) break;
                if (m.collapsed == collapsedAt0) t0 = tMid; else { t1 = tMid; arc1 = m.outerArc; }
            }
            if (arc1 < 0f)
            {
                int s = side;
                if (SampleBranch(map, trims, host, br, t1, ref s, out var e1)) arc1 = e1.outerArc;
            }
            return arc1;
        }

        /// <summary>Host-chain arcs over which the branch has a surface of its
        /// own beside the host, and whether the interval ended by the branch
        /// folding back INTO the host (true) or by it parting or running out
        /// of reach (false: the gap carries a metre on to the nose).</summary>
        static readonly List<(float a0, float a1, bool reclosed)> hostOpen = new List<(float, float, bool)>(4);

        static IEnumerable<int> EmitBranch(CityMap map, Trims trims, TileMeshes tm, Vector2 min, Vector2 max,
                               CityMap.Edge L, CityMap.Edge M, int node)
        {
            var host = BuildChain(map, M, node, linkChain: false, GoreReach + 60f, null);
            var br = BuildChain(map, L, node, linkChain: true, GoreReach + 10f, host);
            foreach (var h in host.edges) foreach (var b in br.edges) clipPairs.Add(PairKey(b.index, h.index));
            foreach (var h in host.edges) goreEdges.Add(h.index);
            foreach (var b in br.edges) goreEdges.Add(b.index);

            int side = 0;
            int quadsFrom = goreQuads.Count;
            bool prevOk = false, havePrev = false;
            Vector3 prevIn = default, prevOut = default;
            float prevArc = 0f;
            int quads = 0;
            float attachedTo = -1f;
            var brOn = new Dictionary<int, (float s0, float s1, int inner)>();    // branch edge -> s range attached
            // THE HOST STANDS DOWN ONLY WHERE THE BRANCH HAS A SURFACE OF ITS
            // OWN BESIDE IT. OSM joins a ramp at the END of its taper, so its
            // first tens of metres lie wholly inside the host, drawn as a
            // zero-width wedge; the host's rail stood down from the node all
            // the same, and 546 m of deck edge city-wide (108 m on I-485) was
            // an open drop with no ramp outside it. The gap is now the host-
            // chain arc over which the branch is NOT collapsed.
            hostOpen.Clear();
            var zoneHere = ZoneAt(L.index, node);
            bool open = false, prevCollapsed = true;
            float openFrom = 0f, openTo = 0f, prevTravelled = 0f;
            BranchSample last = default;
            bool lastOk = false, detached = false;
            yield return 0;
            stepPart = 21;
            for (int k = 0; k <= 70; k++)
            {
                if (k > 0 && k % 10 == 0) yield return 0;   // WP-09: ten stations a step
                float travelled = k * GoreStep;
                if (travelled > GoreReach) break;
                if (!SampleBranch(map, trims, host, br, travelled, ref side, out var smp)) break;
                if (!smp.attached) { detached = true; break; }
                attachedTo = travelled;
                var E = smp.E; var H = smp.H;

                if (!smp.collapsed && !open)
                {
                    openFrom = k == 0 ? 0f : RefineCollapse(map, trims, host, br, prevTravelled, travelled, side, prevCollapsed);
                    if (openFrom < 0f) openFrom = smp.outerArc;
                    open = true;
                }
                else if (smp.collapsed && open)
                {
                    float back = RefineCollapse(map, trims, host, br, prevTravelled, travelled, side, false);
                    hostOpen.Add((openFrom, back < 0f ? openTo : back, true));
                    open = false;
                }
                if (open) openTo = smp.hostArc;
                prevCollapsed = smp.collapsed; prevTravelled = travelled;

                // the branch's frame: which of ITS vertices faces the host
                var tE = E.TangentAt(smp.sE);
                int innerE = side == 0 ? 0 : (Vector2.Dot(new Vector2(-tE.y, tE.x), smp.rM) * side < 0f ? 1 : -1);
                if (!brOn.TryGetValue(E.index, out var b)) b = (smp.sE, smp.sE, innerE);
                brOn[E.index] = (Mathf.Min(b.s0, smp.sE), Mathf.Max(b.s1, smp.sE), b.inner == 0 ? innerE : b.inner);

                // The painted gore: only once the branch has actually left the
                // host. The guard against a piece end is on the CHAIN's arc:
                // on the piece's own arc it skipped two quads wherever a sample
                // fell within half a metre of a host node (up to 12 m by 4.5 m
                // with no floor at all).
                // roads pass L5: no gore inside a merge zone (the host carries
                // the lane there), nor one quad reaching back into it
                var cutBy = ZoneCutOwner(smp.E, smp.sE);
                bool inCut = cutBy != null;
                bool ok = !inCut && smp.gap > GoreMinGapM && smp.hostArc > 0.5f && smp.hostArc < host.Length - 0.5f;
                // The gore's long sides are chords between samples six metres
                // apart, on each road's offset line; the roads draw their edges
                // mitred at every polyline vertex. On the outside of any bend
                // the two part by a few centimetres to a few decimetres, and
                // with both roads' sides standing down over the gore there was
                // nothing in the slot but the lattice (a 20-50 cm lip at a
                // dozen gores in the roadside audit). The gore now reaches
                // GoreSeamM under each road's pavement an inch below it, so the
                // seam is floored whichever way the chord falls.
                var innerX = smp.inner - smp.rL * (smp.lSign * GoreSeamM);
                var outerX = smp.outer - smp.rM * (side * GoreSeamM);
                var vIn = new Vector3(innerX.x - tm.origin.x, smp.yIn - RoadsideRules.EdgeDropM, innerX.y - tm.origin.z);
                var vOut = new Vector3(outerX.x - tm.origin.x, smp.yOut - RoadsideRules.EdgeDropM, outerX.y - tm.origin.z);
                // The first quad of every attachment was skipped (no previous
                // painted sample), a sliver up to 6 m long with no floor: it
                // is painted now from the host's edge where the gore opens.
                if (ok && havePrev)
                {
                    var pIn = prevOk ? prevIn : prevOut;
                    var mid = (smp.inner + smp.outer + new Vector2(pIn.x + prevOut.x, pIn.z + prevOut.z) + new Vector2(tm.origin.x, tm.origin.z) * 2f) * 0.25f;
                    // every quad, drawn here or by the next tile: a seam strip
                    // beside the gap has to lie under all of them (SeamCeiling)
                    goreQuads.Add((prevOut + tm.origin, vOut + tm.origin, vIn + tm.origin, pIn + tm.origin));
                    if (mid.x >= min.x && mid.x < max.x && mid.y >= min.y && mid.y < max.y)
                    {
                        bool elev = H.ElevatedAt(smp.sM);
                        var bk = buckets[(int)SlotOf(JunctionProfile, SurfaceOf(H, elev))];
                        float v0 = prevArc / 12f, v1 = smp.hostArc / 12f;
                        if (tm.tap != null)
                            tm.tap.gores.Add(new RoadTap.Span { slot = (int)SlotOf(JunctionProfile, SurfaceOf(H, elev)), bucketV = bk.Count, edge = H.index, sA = prevArc, sB = smp.hostArc });
                        bk.Up(prevOut, vOut, vIn, pIn,
                              new Vector2(0f, v0), new Vector2(0f, v1), new Vector2(1f, v1), new Vector2(1f, v0));
                        quads++;
                    }
                }
                prevOk = ok; prevIn = vIn; prevOut = vOut; prevArc = smp.hostArc; havePrev = !inCut;
                if (inCut && cutBy == zoneHere)
                {
                    // the gore opens AT the nose: its first quad starts from the
                    // ramp's end section's inner vertex (the host's edge at N)
                    var zi = zoneHere.endInner;
                    float zy = map.edges[zoneHere.endHost].YAt(zoneHere.endHostS) - RoadsideRules.EdgeDropM;
                    prevIn = prevOut = new Vector3(zi.x - tm.origin.x, zy, zi.y - tm.origin.z);
                    prevArc = zoneHere.D; prevOk = false; havePrev = true;
                }
                last = smp; lastOk = ok;
            }
            // the plan box round the quads this branch painted, for MeetPavement
            if (goreQuads.Count > quadsFrom)
            {
                var gb = new Vector4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
                for (int q = quadsFrom; q < goreQuads.Count; q++)
                {
                    var (qa, qb, qc, qd) = goreQuads[q];
                    gb = new Vector4(Mathf.Min(gb.x, Mathf.Min(Mathf.Min(qa.x, qb.x), Mathf.Min(qc.x, qd.x))), Mathf.Min(gb.y, Mathf.Min(Mathf.Min(qa.z, qb.z), Mathf.Min(qc.z, qd.z))),
                                     Mathf.Max(gb.z, Mathf.Max(Mathf.Max(qa.x, qb.x), Mathf.Max(qc.x, qd.x))), Mathf.Max(gb.w, Mathf.Max(Mathf.Max(qa.z, qb.z), Mathf.Max(qc.z, qd.z))));
                }
                goreGroups.Add((quadsFrom, goreQuads.Count, gb));
            }
            // AuditView's record of this gore's P and N (audits only)
            if (goreLog != null) RecordGore(map, trims, node, L, M, host, side, attachedTo, lastOk, detached, last, open, openFrom);
            if (attachedTo < 0f || side == 0) yield break;
            yield return 0;   // WP-09: the stations, then the clips
            stepPart = 22;
            if (open) hostOpen.Add((openFrom, openTo, false));

            // Every branch piece touched is clipped over its attached range,
            // extended back to the node end it shares with the piece before.
            int lastOn = -1;
            for (int i = 0; i < br.edges.Count; i++) if (brOn.ContainsKey(br.edges[i].index)) lastOn = i;
            for (int i = 0; i < br.edges.Count; i++)
            {
                var E = br.edges[i];
                if (!brOn.TryGetValue(E.index, out var b) || b.inner == 0) continue;
                float s0 = b.s0 - 1f, s1 = b.s1 + 1f;
                int nodeEnd = i == 0 ? node : SharedNode(E, br.edges[i - 1]);
                if (E.a == nodeEnd) s0 = -1f; else if (E.b == nodeEnd) s1 = E.length + 1f;
                if (!clips.TryGetValue(E.index, out var list)) clips[E.index] = list = new List<Clip>(2);
                list.Add(new Clip { host = host, sFrom = s0, sTo = s1, side = side, innerSide = b.inner });
                // The inner side stands down back to the node, but at its FAR
                // end only to the last attached sample, where the painted gore
                // stops: the nose beyond runs straight on along the host while
                // the branch's edge bends away, and the sliver between the two
                // over the last metres of the gap had neither gore nor verge.
                // That is the LAST attached piece's far end only. A piece the
                // attachment carries on past runs its gap on through its far
                // node: the gore is painted from its last sample to the next
                // piece's first, up to six metres, and a gap ending at the last
                // sample stood that side's verge (or, on a deck, its rail) up
                // in the painted gore until the node.
                bool carriesOn = i < lastOn;
                float g0 = E.a == nodeEnd ? s0 - 1f : carriesOn ? -1f : b.s0;
                float g1 = E.b == nodeEnd ? s1 + 1f : carriesOn ? E.length + 1f : b.s1;
                goreGaps.Add((E.index, b.inner, g0, g1));
            }
            // ...and every host piece stands its barrier, rail and verge down
            // over the open intervals: from where the branch's outer edge
            // leaves the host's (the refinement lands a hair PAST it, measured
            // at the outer vertex, so the host's rail meets the branch's outer
            // rail on one line and the host's verge meets the verge on the
            // branch's wedge span with no hole between them), to one metre past
            // the last attached sample (where the nose block below picks up on
            // a deck) — or, where the branch folds back into the host, to the
            // crossing itself, where the branch's wedge rail ends.
            // roads pass L5: a merge zone's host stands down exactly from its
            // nose N on - its widened edge keeps its verge (or rail) right up
            // to the ramp's end section, which lies on that cross-line
            var zone = zoneHere;
            foreach (var (a0, a1, reclosed) in hostOpen)
            {
                float c0 = a0, c1 = reclosed ? a1 : a1 + 1f;
                if (zone != null && a0 < zone.D + 6f) c0 = zone.D;
                if (c1 <= c0) continue;
                foreach (var H in host.edges)
                {
                    if (!host.PieceRange(H, out float p0, out float p1) || p1 <= c0 || p0 >= c1) continue;
                    if (!host.Walk(Mathf.Clamp(0.5f * (Mathf.Max(c0, p0) + Mathf.Min(c1, p1)), 0f, host.Length), out _, out float sMid, out _, out var dMid)) continue;
                    var tH = H.TangentAt(sMid);
                    int sideH = side * (Vector2.Dot(dMid, tH) >= 0f ? 1 : -1);
                    float s0 = ArcOnPiece(host, H, c0, p0, p1), s1 = ArcOnPiece(host, H, c1, p0, p1);
                    goreGaps.Add((H.index, sideH, Mathf.Min(s0, s1), Mathf.Max(s0, s1)));
                }
            }
            // a nose only where the two actually part (not where a chain ran out)
            if (lastOk && detached) EmitNose(map, trims, tm, min, max, host, br, last, side, attachedTo, quadsFrom);
            if (quads > 0) tm.goreCount++;
            tm.branchCount++;
        }

        /// <summary>A host-chain arc as an arc on one of its pieces; past
        /// either end of the piece, a metre beyond that end so a gap that
        /// runs on into the next piece does not let the rail restart at the
        /// node between them.</summary>
        static float ArcOnPiece(Chain host, CityMap.Edge H, float c, float p0, float p1)
        {
            if (c <= p0 || c >= p1)
            {
                host.Walk(Mathf.Clamp(c <= p0 ? p0 + 0.01f : p1 - 0.01f, 0f, host.Length), out _, out float sEnd, out _, out _);
                return sEnd < H.length * 0.5f ? -1f : H.length + 1f;
            }
            host.Walk(c, out _, out float s, out _, out _);
            return s;
        }

        /// <summary>
        /// THE GORE NOSE. The painted gore ends at the last attached sample,
        /// and its far edge — up to 4.5 m wide, 2.9 m at the median — faced
        /// the open V between two diverging decks with nothing across it (45
        /// such noses on structure city-wide). On a deck it gets a rail block
        /// across it that carries on along the host's edge to where the host
        /// rail restarts and along the branch's inner edge to where its rail
        /// does, so the three meet. On the ground the nose gets a verge.
        /// </summary>
        static void EmitNose(CityMap map, Trims trims, TileMeshes tm, Vector2 min, Vector2 max,
                             Chain host, Chain br, BranchSample n, int side, float travelled, int quadsFrom)
        {
            var mid = (n.inner + n.outer) * 0.5f;
            if (mid.x < min.x || mid.x >= max.x || mid.y < min.y || mid.y >= max.y) return;
            bool elev = n.H.ElevatedAt(n.sM);
            var o3 = tm.origin;
            var vOut = new Vector3(n.outer.x - o3.x, n.yOut, n.outer.y - o3.z);
            var vIn = new Vector3(n.inner.x - o3.x, n.yIn, n.inner.y - o3.z);
            var fwd = n.dirM;
            tm.goreNoses.Add((vOut + o3, vIn + o3, new Vector3(fwd.x, 0f, fwd.y), elev));
            // On the ground the nose is warranted a block only by the same
            // drop a ribbon's side would be.
            bool warranted = elev || (DropFrom(map, trims, mid, fwd, 0.5f * (n.yIn + n.yOut), VergeShoulderM) & DropWarrant) != 0;
            // ...and not where the V beyond it is paved at its height: where a
            // chain of short links meets at a junction on a bridge, the nose
            // fell inside the junction's fan and its block stood across the
            // lanes of the arms beyond (Tyvola Road's node 14060, the Freedom
            // Drive cluster at nodes 458 and 13286; 2026-09-14).
            if (warranted && PavedOnward(map, trims, n.outer, n.inner, n.yOut, n.yIn, fwd, -1)) warranted = false;
            if (!warranted)
            {
                // Along the painted gore's far edge, which reaches GoreSeamM
                // under each road's pavement (EmitBranch): the offsets lie
                // along each road's own normal, so a verge laid from the two
                // edge points themselves ran on a different line, and the
                // sliver between the gore's last quad and the verge's first
                // stood open to the lattice a third of a metre down (I-277's
                // e2340 and e732, North Tryon Street's e14612).
                var inX = n.inner - n.rL * (n.lSign * GoreSeamM);
                var outX = n.outer - n.rM * (side * GoreSeamM);
                if (groundLog != null) groundTag = "gore nose verge";
                EmitVergeLine(map, trims, tm, new Vector3(outX.x - o3.x, n.yOut, outX.y - o3.z),
                              new Vector3(inX.x - o3.x, n.yIn, inX.y - o3.z), fwd, fwd, VergeShoulderM);
                return;
            }
            float drop = elev ? CityElevation.DeckThick : RailBuryM;
            float v0 = n.hostArc / RoadVTile;
            // across the nose, its traffic face toward the gore; each of the
            // three pieces only where it stands in no lane (RailRunsOffLanes)
            EmitRailOffLanes(map, trims, o3, vOut, vIn, -fwd, -fwd, drop, v0, v0 + 0.5f, -1, new RailRecord { edge = -1, node = -2 });
            // ...AND A SLOPED V IN FRONT OF IT (hotfix 2026-10-03: the block's
            // face stood square to the gore's traffic - the owner's "concrete
            // wall ... steps at 90 degrees into the carriageway area" on
            // I-277). Two rails from an apex on the painted gore's middle line
            // to the nose's two ends, each rising from a curb at the apex to
            // full height at the block: every face the traffic can reach
            // glances off. Their traffic faces end on the two pavements' edges
            // (the legs stand in the gore), and each only where it stands in no
            // lane (RailRunsOffLanes).
            {
                float halfW = 0.5f * Vector2.Distance(new Vector2(vOut.x, vOut.z), new Vector2(vIn.x, vIn.z));
                // The apex on the painted gore's middle, walked back over this
                // branch's gore quads (each a cross-line from the host's edge to
                // the branch's) as far as 1:3 legs want, and no further than the
                // gore is NoseApexRoomM wide: the legs then stand inside the
                // gore's wedge, out of both roads' lanes. A nose with no such
                // room keeps its block alone.
                var mid3 = 0.5f * (vOut + vIn);
                float want = Mathf.Max(TaperLenM, 3f * halfW), acc = 0f, noseLen = 0f;
                var apex = mid3;
                var prevMid = mid3 + o3;
                float prevW = 2f * halfW;
                for (int q = goreQuads.Count - 1; q >= quadsFrom && 2f * halfW >= NoseApexRoomM; q--)
                {
                    var (qOut, _, _, qIn) = goreQuads[q];
                    var m = 0.5f * (qOut + qIn);
                    float seg = Vector2.Distance(new Vector2(m.x, m.z), new Vector2(prevMid.x, prevMid.z));
                    // (a gore quad reaches GoreSeamM under each road's pavement)
                    float w = Vector2.Distance(new Vector2(qOut.x, qOut.z), new Vector2(qIn.x, qIn.z)) - 2f * GoreSeamM;
                    // narrower than the apex needs here: the apex goes where
                    // the gore narrows to it, between this cross-line and the last
                    float f = w >= NoseApexRoomM ? 1f : Mathf.Clamp01((prevW - NoseApexRoomM) / Mathf.Max(1e-3f, prevW - w));
                    var mm = Vector3.Lerp(prevMid, m, f);
                    if (acc + seg * f >= want) { mm = Vector3.Lerp(prevMid, m, (want - acc) / Mathf.Max(1e-3f, seg)); f = 0f; }
                    acc += Vector2.Distance(new Vector2(mm.x, mm.z), new Vector2(prevMid.x, prevMid.z));
                    apex = new Vector3(mm.x - o3.x, mid3.y, mm.z - o3.z); noseLen = acc;
                    if (w < NoseApexRoomM || f == 0f) break;
                    prevMid = m; prevW = w;
                }
                var acrossOI = new Vector2(vOut.x - vIn.x, vOut.z - vIn.z);
                var inward = new Vector3(-acrossOI.x, 0f, -acrossOI.y) / Mathf.Max(1e-3f, acrossOI.magnitude);
                for (int leg = 0; leg < 2 && noseLen >= 1f; leg++)
                {
                    // the leg's line ends a rail's width into the gore, so its
                    // traffic face ends on that pavement's edge
                    var end = leg == 0 ? vOut + inward * Mathf.Min(RailW, 0.45f * halfW) : vIn - inward * Mathf.Min(RailW, 0.45f * halfW);
                    var dl = new Vector2(end.x - apex.x, end.z - apex.z);
                    if (dl.sqrMagnitude < 1e-4f) continue;
                    var perp = new Vector2(-dl.y, dl.x).normalized;
                    // the traffic face toward the lanes this leg's end sits beside
                    if (Vector2.Dot(perp, leg == 0 ? acrossOI : -acrossOI) < 0f) perp = -perp;
                    EmitRailOffLanes(map, trims, o3, apex, end, perp, perp, drop, v0 - noseLen / RoadVTile, v0, -1,
                                     new RailRecord { edge = -1, node = -2 }, TaperFootM, RailH);
                }
            }
            // along the host's edge to its rail's restart (the gap ends one
            // metre past the last attached sample)
            if (host.Walk(Mathf.Min(n.hostArc + 1f, host.Length), out var Hn, out float sHn, out var pHn, out var dHn))
            {
                var rHn = new Vector2(-dHn.y, dHn.x);
                float hw = HostHalf(map, trims, Hn, sHn, rHn, side);
                var e = pHn + rHn * (side * hw);
                var inH = -rHn * side;
                EmitRailOffLanes(map, trims, o3, vOut, new Vector3(e.x - o3.x, Hn.YAt(sHn), e.y - o3.z), inH, inH, drop, v0, v0 + 1f / RoadVTile, -1,
                                 new RailRecord { edge = Hn.index, side = side, node = -2, s0 = sHn, s1 = sHn });
            }
            // along the branch's inner edge to its rail's restart (two metres)
            if (br.Walk(Mathf.Min(travelled + 2f, br.Length), out var Eb, out float sEb, out var pEb, out var dEb))
            {
                var rLb = new Vector2(-dEb.y, dEb.x);
                var e = EdgePoint(Eb, sEb, pEb, rLb, n.lSign);
                var inB = -rLb * n.lSign;
                EmitRailOffLanes(map, trims, o3, vIn, new Vector3(e.x - o3.x, Eb.YAt(sEb), e.y - o3.z), inB, inB, drop, v0, v0 + 2f / RoadVTile, -1,
                                 new RailRecord { edge = Eb.index, side = n.lSign < 0f ? -1 : 1, node = -2, s0 = sEb, s1 = sEb });
            }
        }

        static int SharedNode(CityMap.Edge e, CityMap.Edge prev) =>
            e.a == prev.a || e.a == prev.b ? e.a : e.b;

        /// <summary>
        /// Where one branch runs BESIDE its host, in plan only: the pieces of
        /// the branch chain, walked away from the node, over the arc each lies
        /// within a gore of the host's pavement, and the host chain to ask
        /// for the host's surface under any point of them.
        ///
        /// The elevation solver needs this before any tile exists. The walk is
        /// EmitBranch's own — same chains, same step, same gore width — with
        /// two differences: no height test (making the heights agree is the
        /// point of asking), and the host's UNSQUEEZED half width (a squeeze
        /// needs heights too). The unsqueezed host is never narrower, so this
        /// zone is never shorter than the one a tile clips, which is the
        /// direction that matters: a ramp the tile still calls attached must
        /// already be seated.
        /// </summary>
        public class Seat
        {
            public int node;
            /// <summary>Branch pieces beside the host: edge, arc range, and
            /// which way the chain walks it (+1 = from its a end).</summary>
            public readonly List<(int edge, float s0, float s1, int dir)> pieces = new List<(int, float, float, int)>(2);
            /// <summary>Where the branch's inner edge lies INSIDE the host's
            /// edge (its lanes run into the host's): edge and arc range. A
            /// tile cuts the branch against its host there, and any height
            /// between the two is a seam across the lanes.</summary>
            public readonly List<(int edge, float s0, float s1)> overlaps = new List<(int, float, float)>(2);
            internal Chain host;

            /// <summary>The host edge and arc under a plan point, or -1 where
            /// the host chain has run out.</summary>
            public int HostAt(Vector2 p, out float hostS)
            {
                host.Project(p, out var H, out hostS, out _, out _, out bool atEnd);
                return atEnd ? -1 : H.index;
            }
        }

        /// <summary>Every branch end's <see cref="Seat"/>, from the trims'
        /// branch table. Plan geometry only; deterministic.</summary>
        public static List<Seat> BranchSeats(CityMap map, Trims trims, bool streetsOnly = false)
        {
            var result = new List<Seat>();
            for (int ei = 0; ei < map.edges.Length; ei++)
                for (int end = 0; end < 2; end++)
                {
                    int hostIdx = end == 0 ? trims.branchA[ei] : trims.branchB[ei];
                    if (hostIdx < 0) continue;
                    var L = map.edges[ei];
                    // RAMPS ONLY. A street fork's two carriageways are branches
                    // too, but seating one on the other copied the other's
                    // short-sliver cliffs onto a hundred metres of Parkwood
                    // Avenue; the staircase that was reported is a ramp.
                    // (streetsOnly: the other branches, for the vertical
                    // curves' SOFT seats - CityElevation.VerticalCurves)
                    if (L.link == streetsOnly) continue;
                    var seat = SeatOf(map, trims, L, map.edges[hostIdx], end == 0 ? L.a : L.b);
                    if (seat != null) result.Add(seat);
                }
            return result;
        }

        static Seat SeatOf(CityMap map, Trims trims, CityMap.Edge L, CityMap.Edge M, int node)
        {
            var host = BuildChain(map, M, node, linkChain: false, GoreReach + 60f, null);
            var br = BuildChain(map, L, node, linkChain: true, GoreReach + 10f, host);
            int side = 0;
            var on = new Dictionary<int, (float s0, float s1)>();
            var into = new Dictionary<int, (float s0, float s1)>();
            for (int k = 0; k <= 70; k++)
            {
                float travelled = k * GoreStep;
                if (travelled > GoreReach) break;
                if (!br.Walk(travelled, out var E, out float sE, out var p, out var dirL)) break;
                host.Project(p, out var H, out float sM, out var q, out var dirM, out bool hostOut);
                if (hostOut) break;
                var rM = new Vector2(-dirM.y, dirM.x);
                float off = Vector2.Dot(p - q, rM);
                int sideNow = off >= 0f ? 1 : -1;
                if (side == 0 && Mathf.Abs(off) > 0.4f) side = sideNow;
                var rL = new Vector2(-dirL.y, dirL.x);
                var outer = side == 0 ? q : EdgePoint(H, sM, q, rM, side);
                float lSign = Vector2.Dot(rL, rM) >= 0f ? -side : side;
                var inner = EdgePoint(E, sE, p, rL, lSign);
                float gap = side == 0 ? 0f : Vector2.Dot(inner - outer, rM) * side;
                bool beside = (gap <= GoreMaxGap && sideNow == side) || Mathf.Abs(off) < 0.4f;
                if (!beside) break;
                on[E.index] = on.TryGetValue(E.index, out var r)
                    ? (Mathf.Min(r.s0, sE), Mathf.Max(r.s1, sE)) : (sE, sE);
                if (gap < 0f || Mathf.Abs(off) < 0.4f)
                    into[E.index] = into.TryGetValue(E.index, out var ri)
                        ? (Mathf.Min(ri.s0, sE), Mathf.Max(ri.s1, sE)) : (sE, sE);
            }
            if (on.Count == 0) return null;
            var seat = new Seat { node = node, host = host };
            foreach (var kv in into) seat.overlaps.Add((kv.Key, kv.Value.s0, kv.Value.s1));
            for (int i = 0; i < br.edges.Count; i++)
            {
                var E = br.edges[i];
                if (!on.TryGetValue(E.index, out var r)) continue;
                // extended back to the node end it shares with the piece
                // before, exactly as a tile's clip range is
                int nodeEnd = i == 0 ? node : SharedNode(E, br.edges[i - 1]);
                int dir = E.a == nodeEnd ? 1 : -1;
                seat.pieces.Add((E.index, dir > 0 ? 0f : r.s0, dir > 0 ? r.s1 : E.length, dir));
            }
            return seat;
        }

        static bool InGoreGap(int edge, int side, float s0, float s1)
        {
            foreach (var g in goreGaps)
                if (g.edge == edge && g.side == side && s1 > g.s0 && s0 < g.s1) return true;
            return false;
        }

        /// <summary>
        /// Does a SPAN between two sections stand in a gap? SamplePositions
        /// puts a section at every gap end, but drops one that falls within
        /// half a metre of a section already there — and a span that then
        /// overlapped the gap by those few centimetres stood its whole side
        /// down: ten metres of a ramp's inner edge past the end of its gore
        /// with neither verge nor gore under it (a 17-24 cm lip along I-277's
        /// East 3rd Street ramp). The span is shrunk by that tolerance first.
        /// </summary>
        static bool SpanInGoreGap(int edge, int side, float s0, float s1)
        {
            float slack = Mathf.Min(0.55f, (s1 - s0) * 0.25f);
            return InGoreGap(edge, side, s0 + slack, s1 - slack);
        }

        // ------------------------------------------------------------------
        //  Road ribbons, kerbs, decks, rails, barriers, piers.
        // ------------------------------------------------------------------

        /// <summary>One cross-section of a ribbon.</summary>
        struct Section
        {
            public float s;
            public Vector3 L, R;      // tile-local, L = right of travel (see below)
            public Vector2 right;     // map-view unit, R - L direction
            public float hw;          // the half width it was cut from, taper and mitre applied
            /// <summary>The OSM line's point (world plan) and the ribbon's
            /// centre off it along right (the line model: lanes kept in place,
            /// a lane added on one side).</summary>
            public Vector2 P;
            public float centre;
            public bool elev;
            public bool clippedIn;    // the inner edge was moved onto the host: no kerb there
            /// <summary>Roads pass L5: a ramp's last section, laid on its host's
            /// cross-line at the nose N (MeetPavement leaves it alone).</summary>
            public bool zoneEnd;
            public bool collapsed;    // zero width: the whole ribbon is inside the host
            public int innerSide;     // -1 / +1 which vertex is the inner one while clipped, 0 otherwise
            /// <summary>A parallel neighbour this section was squeezed against
            /// on that side (edge index, or -1), the arc on it, and the width
            /// of the strip left between the two drawn edges: the two share
            /// one barrier or one rail, and a verge only floors half the strip.</summary>
            public int nbL, nbR;
            public float nbAtL, nbAtR, stripL, stripR;
            /// <summary>The squeeze moved that edge in (SqueezeSection): the
            /// smoothness gate's tap reads the cause, not the drawn position.</summary>
            public bool sqL, sqR;
            /// <summary>Texture U at each vertex, from its TRUE lateral
            /// offset. A squeezed or clipped ribbon crops the painted
            /// profile instead of compressing it, so the lane lines stay
            /// where the lanes are; compressed, they slalomed wherever a
            /// neighbour's mapped line wandered.</summary>
            public float uL, uR;

            public Vector3 Edge(int side) => side < 0 ? L : R;
            /// <summary>Outward from the ribbon on a side, in map view.</summary>
            public Vector2 Out(int side) => side < 0 ? -right : right;
            public int Nb(int side) => side < 0 ? nbL : nbR;
            public float NbAt(int side) => side < 0 ? nbAtL : nbAtR;
            public float Strip(int side) => side < 0 ? stripL : stripR;
        }
        /// <summary>Edge pairs that are host and branch: they overlap by
        /// design and are never squeezed against each other.</summary>
        static readonly HashSet<long> clipPairs = new HashSet<long>();
        static long PairKey(int a, int b) => ((long)Mathf.Min(a, b) << 24) | (long)Mathf.Max(a, b);
        static readonly List<Section> sections = new List<Section>(64);
        static readonly List<float> sampleS = new List<float>(64);

        /// <summary>
        /// Where a ribbon is sampled: every elevation station, every polyline
        /// vertex (with a mitred cross-section, so a bend inside an edge does
        /// not pinch), the taper ends, and the exact trims.
        /// </summary>
        static void SamplePositions(CityMap map, CityMap.Edge e, Trims trims, float sMin, float sMax)
        {
            sampleS.Clear();
            sampleS.Add(sMin);
            for (int i = 0; i < e.stS.Length; i++) if (e.stS[i] > sMin + 0.6f && e.stS[i] < sMax - 0.6f) sampleS.Add(e.stS[i]);
            for (int i = 1; i + 1 < e.s.Length; i++) if (e.s[i] > sMin + 0.6f && e.s[i] < sMax - 0.6f) sampleS.Add(e.s[i]);
            // the line model's tapers: their ends (where a line starts or
            // ends) and enough sections between that the eased edge is
            // faceted within 2 cm (plan A2, I5)
            LineModel.TaperSamples(e, sMin, sMax, sampleS);
            // A clipped branch's inner edge is the host's edge: it has to
            // bend wherever the host bends, or a 10 m straight across a
            // kink in the mainline leaves a sliver of nothing between them.
            // And it has to be sampled where the cut STARTS, where it
            // COLLAPSES and wherever it moves fast: a road meeting its host
            // at forty-five degrees is cut across its whole width in ten
            // metres, and one section every ten metres drew that as a
            // triangle with a hole beside it.
            if (clips.TryGetValue(e.index, out var clist))
                foreach (var c in clist)
                {
                    foreach (var v in c.host.pts)
                    {
                        CityElevation.ProjectOn(e, v, out float sOn);
                        if (sOn < c.sFrom || sOn > c.sTo || sOn <= sMin + 0.6f || sOn >= sMax - 0.6f) continue;
                        if ((e.PointAt(sOn) - v).sqrMagnitude > 40f * 40f) continue;
                        sampleS.Add(sOn);
                    }
                    // ...and wherever the host's grade breaks. The inner vertex
                    // takes the host's height (ClipSection), which is straight
                    // between the host's stations; ten metres of branch edge
                    // straight across a sag in the host stood 5.5 cm over the
                    // host's edge at the bottom of it (e1409 beside the East
                    // Independence Expressway's station at 318 m).
                    foreach (var H in c.host.edges)
                        for (int k = 1; k + 1 < H.stS.Length; k++)
                        {
                            var v = H.PointAt(H.stS[k]);
                            CityElevation.ProjectOn(e, v, out float sOn);
                            if (sOn < c.sFrom || sOn > c.sTo || sOn <= sMin + 0.6f || sOn >= sMax - 0.6f) continue;
                            if ((e.PointAt(sOn) - v).sqrMagnitude > 40f * 40f) continue;
                            sampleS.Add(sOn);
                        }
                    float from = Mathf.Max(sMin, c.sFrom), to = Mathf.Min(sMax, c.sTo);
                    int prevState = -1; float prevLam = 0f, prevS = from;
                    for (float sc = from; sc <= to + 0.01f; sc += 1f)
                    {
                        var pc = e.PointAt(sc);
                        var tc = e.TangentAt(sc);
                        var rc = new Vector2(-tc.y, tc.x);
                        int state = 0; float lam = 0f;
                        LineModel.CentreAt(e, sc, out float cc, out float hc);
                        if (CutAgainstHost(map, trims, e, sc, pc + rc * cc, rc, hc, e.YAt(sc), c, out lam, out bool col))
                            state = col ? 2 : 1;
                        bool change = prevState >= 0 && (state != prevState || (state == 1 && Mathf.Abs(lam - prevLam) > 0.8f));
                        if (change)
                        {
                            if (prevS > sMin + 0.6f && prevS < sMax - 0.6f) sampleS.Add(prevS);
                            if (sc > sMin + 0.6f && sc < sMax - 0.6f) sampleS.Add(sc);
                        }
                        prevState = state; prevLam = lam; prevS = sc;
                    }
                }
            // Where a rail, barrier or verge stands down or picks up again,
            // exactly: a gap's end fell between sections and the rail
            // restarted at the next one, up to ten metres late (the gore
            // nose's two inner rails). And the approach rail's reach past
            // each structure end.
            foreach (var g in goreGaps)
            {
                if (g.edge != e.index) continue;
                if (g.s0 > sMin + 0.6f && g.s0 < sMax - 0.6f) sampleS.Add(g.s0);
                if (g.s1 > sMin + 0.6f && g.s1 < sMax - 0.6f) sampleS.Add(g.s1);
            }
            StructureEnds(map, trims, e, endScratch);
            foreach (var se in endScratch)
                for (int k = -1; k <= 1; k += 2)
                {
                    float sr = se + k * ApproachRailM;
                    if (sr > sMin + 0.6f && sr < sMax - 0.6f) sampleS.Add(sr);
                }
            // plan A2: where a union run starts and ends, so its side flags
            // (no rail, the median) switch exactly there
            unionEndScratch.Clear();
            var uruns = trims.unions != null ? trims.unions[e.index] : null;
            if (uruns != null)
                foreach (var ur in uruns)
                    for (int k = 0; k < 2; k++)
                    {
                        float su = k == 0 ? ur.s0 : ur.s1;
                        // not inside a clip: the clip's inner side is a gap there, and a
                        // section added inside a clipped ramp moved its rail (e14947)
                        if (su > sMin + 0.6f && su < sMax - 0.6f && ClipAt(e, su) == null) { sampleS.Add(su); unionEndScratch.Add(su); }
                    }
            sampleS.Add(sMax);
            sampleS.Sort();
            // Drop near-duplicates (a station on a vertex) — and where one of a
            // pair IS a polyline vertex, keep the vertex. Only a section at the
            // vertex is mitred; one a hand's width short of it is square to the
            // segment before, and the chord from it to the next section cut the
            // corner of the bend: on a 40 degree bend of Shenandoah Avenue the
            // drawn edge stood 0.9 m inside the road's own, its verge out there
            // falling away under the lattice. A run's exact end is not traded
            // for a vertex, though (IsRunEndArc).
            int w = 1;
            for (int i = 1; i < sampleS.Count; i++)
            {
                if (sampleS[i] - sampleS[w - 1] > 0.5f || i == sampleS.Count - 1) { sampleS[w++] = sampleS[i]; continue; }
                if (w > 1 && IsVertexArc(e, sampleS[i]) && !IsVertexArc(e, sampleS[w - 1]) && !IsRunEndArc(e, sampleS[w - 1]))
                    sampleS[w - 1] = sampleS[i];
            }
            sampleS.RemoveRange(w, sampleS.Count - w);
            // roads pass L6: where a lane-use mark starts and ends (before the
            // zones', whose clear ranges keep the step at N clean)
            MarkForcedSamples(map, trims, e, sMin, sMax);
            // roads pass L5: the merge zones' exact sections (the host's step
            // at N, the ramp's end section)
            ZoneForcedSamples(e, sMin, sMax);
        }

        /// <summary>Is an arc position one SamplePositions adds so a run
        /// stands down or picks up exactly there — a gore gap's end, a
        /// structure end, an approach rail's reach past one? That section is
        /// kept over a polyline vertex a hand's width on: traded for the
        /// vertex, 42 such ends on the audited tiles moved up to half a metre
        /// late, past the tolerance SpanInGoreGap allows a gap end (a side
        /// standing down past its gore, a deck rail restarting that late).
        /// Reads the gaps and structure ends SamplePositions has just
        /// read.</summary>
        static bool IsRunEndArc(CityMap.Edge e, float s)
        {
            foreach (var g in goreGaps)
                if (g.edge == e.index && (Mathf.Abs(g.s0 - s) < 1e-4f || Mathf.Abs(g.s1 - s) < 1e-4f)) return true;
            foreach (var se in endScratch)
                if (Mathf.Abs(se - s) < 1e-4f || Mathf.Abs(Mathf.Abs(se - s) - ApproachRailM) < 1e-4f) return true;
            foreach (var su in unionEndScratch)
                if (Mathf.Abs(su - s) < 1e-4f) return true;
            return false;
        }
        static readonly List<float> unionEndScratch = new List<float>(8);

        /// <summary>Is an arc position exactly one of the edge's interior
        /// polyline vertices, as SamplePositions adds them?</summary>
        static bool IsVertexArc(CityMap.Edge e, float s)
        {
            int k = System.Array.BinarySearch(e.s, s);
            return k > 0 && k < e.s.Length - 1;
        }

        /// <summary>The cross-section direction at an arc position: the
        /// segment tangent, or at a polyline vertex the mitre of its two
        /// segments (widened by 1/cos of the half turn, capped), and at a
        /// mitred NODE the mitre with the continuing edge.</summary>
        static Vector2 RightAt(CityMap map, Trims trims, CityMap.Edge e, float s, out float widen)
        {
            widen = 1f;
            Vector2 tan;
            int seg = e.SegmentAt(s, out float t);
            bool atVertexA = seg > 0 && t < 1e-3f;
            bool atVertexB = seg + 2 < e.s.Length && t > 1f - 1e-3f;
            if (atVertexA || atVertexB)
            {
                int v = atVertexA ? seg : seg + 1;
                Vector2 t0 = (e.pts[v] - e.pts[v - 1]).normalized;
                Vector2 t1 = (e.pts[v + 1] - e.pts[v]).normalized;
                tan = (t0 + t1);
                if (tan.sqrMagnitude < 1e-6f) tan = t0; else tan.Normalize();
                // The bisector direction, at the plain half width. The true
                // mitre offset is hw / cos(half turn); widening by that put
                // the host's edge outside every extent the clips and the
                // audit computed (a rail 20 cm into the ramp beside it), and
                // the notch this leaves at the outside of a bend is a few
                // centimetres on any bend OSM draws.
            }
            else if (s <= 1e-3f && trims.mitre[e.a] && IsThrough(trims, e, e.a))
            {
                tan = MitreAt(map, trims, e, e.a, out widen);
            }
            else if (s >= e.length - 1e-3f && trims.mitre[e.b] && IsThrough(trims, e, e.b))
            {
                tan = MitreAt(map, trims, e, e.b, out widen);
            }
            else tan = e.TangentAt(s);
            return new Vector2(-tan.y, tan.x);
        }

        // ------------------------------------------------------------------
        //  PAINT COLUMNS (WP-11b; plan A2 I2/I3, A7): a span whose two
        //  sections are the full model width and nearly parallel is one quad
        //  with U 1..0 across it - a constant width maps U exactly. Anything
        //  else (a taper, a squeeze, a clip, a sharper bend) is drawn as a row
        //  of strips: each painted line its own 12 cm strip at the line's
        //  texels (U fixed, never against the section's width), the pavement
        //  between lines from a paint-free band. So no texture is squeezed, no
        //  diagonal bends a line, and paint appears only where the model has
        //  a line.
        // ------------------------------------------------------------------
        static readonly List<LineModel.LineAt> linesA = new List<LineModel.LineAt>(16), linesB = new List<LineModel.LineAt>(16);
        static readonly List<(int k, float latA, float latB)> spanLines = new List<(int, float, float)>(16);
        /// <summary>Spans drawn as one quad and as paint columns (for the
        /// budget probe).</summary>
        public static long RibbonQuads, RibbonColumnSpans, RibbonStrips;

        static float LatOf(in Section c, Vector3 v) =>
            Vector2.Dot(new Vector2(v.x + tileOrigin.x, v.z + tileOrigin.z) - c.P, c.right);

        /// <summary>Draws one ribbon span; returns the strips it drew as
        /// columns (0: one plain quad).</summary>
        static int EmitRibbon(Bucket bk, TileMeshes tm, CityMap.Edge e, in Section A, in Section B, float v0, float v1)
        {
            float latLA = LatOf(A, A.L), latRA = LatOf(A, A.R), latLB = LatOf(B, B.L), latRB = LatOf(B, B.R);
            LineModel.Extents(e, A.s, out float eMA, out float ePA);
            LineModel.Extents(e, B.s, out float eMB, out float ePB);
            float fullP = e.lmPlus != 0f || e.lmMinus != 0f ? e.lmPlus : e.width * 0.5f;
            float fullM = e.lmPlus != 0f || e.lmMinus != 0f ? e.lmMinus : e.width * 0.5f;
            bool Full(in Section c, float eM, float eP, float lL, float lR) =>
                !c.collapsed && !c.clippedIn && !c.sqL && !c.sqR
                && Mathf.Abs(eM - fullM) < 1e-3f && Mathf.Abs(eP - fullP) < 1e-3f
                && Mathf.Abs(lL + eM) < 0.02f && Mathf.Abs(lR - eP) < 0.02f;
            // a U=c line across two sections turned by theta strays laterally
            // by w (1 - cos theta) / 2 at most (w the half width): within V/2
            float w = 0.5f * Mathf.Max(latRA - latLA, latRB - latLB);
            bool bendOk = w * (1f - Vector2.Dot(A.right, B.right)) * 0.5f <= 0.0125f;
            bool full = Full(A, eMA, ePA, latLA, latRA) && Full(B, eMB, ePB, latLB, latRB) && bendOk
                        // a relay (roads pass L4) moves the lines off the texture's columns
                        && !LineModel.Relayed(e, A.s) && !LineModel.Relayed(e, B.s);
            // THE EDGE'S OWN LINES (roads pass L2): its line set on its
            // profile; the texture's own layout when they are the same
            var lay = LineModel.LayoutOf(e);
            var tex = lay.tex ?? lay;
            float tw = tex.texW;
            // roads pass L6: a span carrying lane-use paint is drawn as columns
            bool marks = lay.m.Length > 0 && tex.m.Length > 0 && SpanMarks(e, A.s, B.s);
            if (full && lay.isDefault && !marks)
            {
                // U = 0 on the left of travel (the R vertex), so a one-way
                // carriageway's narrow inside shoulder and wide outside
                // shoulder land where the painter put them. The winding
                // (near-left, far-left, far-right, near-right) is the
                // verified face-up order.
                bk.Quad(A.L, B.L, B.R, A.R,
                    new Vector2(1f, v0), new Vector2(1f, v1), new Vector2(0f, v1), new Vector2(0f, v0));
                RibbonQuads++;
                return 0;
            }
            if (A.collapsed || B.collapsed || latRA - latLA < 0.05f || latRB - latLB < 0.05f)
            {
                // nothing of its own to paint: the shoulder's paint-free texels
                float g0 = 0.25f / tw, g1 = (tex.x0[0] - 0.25f) / tw;
                bk.Quad(A.L, B.L, B.R, A.R, new Vector2(g1, v0), new Vector2(g1, v1), new Vector2(g0, v1), new Vector2(g0, v0));
                RibbonQuads++;
                return 0;
            }
            if (lay.m.Length == 0)
            {
                // an UNMARKED street (owner Q1): one quad of plain pavement,
                // the texture's widest paint-free texels
                float g0 = (tex.bandLo + 0.25f) / tw, g1 = (tex.bandHi + 0.75f) / tw;
                bk.Quad(A.L, B.L, B.R, A.R, new Vector2(g1, v0), new Vector2(g1, v1), new Vector2(g0, v1), new Vector2(g0, v0));
                RibbonQuads++;
                return 0;
            }
            DrawnLines(e, lay, A, eMA, ePA, latLA, latRA, linesA);
            DrawnLines(e, lay, B, eMB, ePB, latLB, latRB, linesB);
            // the span draws a line only where both sections have it
            spanLines.Clear();
            for (int i = 0, j = 0; i < linesA.Count && j < linesB.Count;)
            {
                if (linesA[i].k == linesB[j].k) { spanLines.Add((linesA[i].k, linesA[i].lat, linesB[j].lat)); i++; j++; }
                else if (linesA[i].k > linesB[j].k) i++; else j++;
            }
            float PH = lay.half;
            if (marks) return EmitMarked(bk, e, A, B, v0, v1, lay, tex, latLA, latRA, latLB, latRB);
            int strips = 0;
            // IDENTITY RUNS (L2): a full-width span of an edge whose lines are
            // not its texture's draws every stretch where the texture already
            // shows the right thing - the same line at the same place, or
            // pavement with no paint under it - as ONE strip at the texture's
            // own U (u = (plus - lat) / W: exactly the quad's mapping), and
            // only the differences as strips of their own
            bool ident = full && !lay.isDefault;
            float fullPl = e.lmPlus != 0f || e.lmMinus != 0f ? e.lmPlus : e.width * 0.5f;
            float runA = latLA, runB = latLB;           // where the pending identity strip starts
            bool runOpen = false;
            // walk from the L edge to the R edge
            float cA = latLA, cB = latLB;               // the left column of the next strip
            int prevK = -1;                              // the line just L-ward, or -1 (the L edge)
            for (int i = 0; i <= spanLines.Count; i++)
            {
                bool edge = i == spanLines.Count;
                float nA = edge ? latRA : spanLines[i].latA - PH, nB = edge ? latRB : spanLines[i].latB - PH;
                int kn = edge ? -1 : spanLines[i].k;
                if (nA - cA > 1e-3f || nB - cB > 1e-3f)
                {
                    // the pavement strip: the paint-free texels on kn's L side (its
                    // larger-m neighbour gap), or beside the last line at the R edge;
                    // on an edge's own layout, the texture's widest paint-free run
                    if (ident && PaintFree(tex, fullPl - nA, fullPl - cA))
                    { if (!runOpen) { runOpen = true; runA = cA; runB = cB; } }
                    else
                    {
                        if (runOpen) { strips += IdentStrip(bk, A, B, latLA, latRA, latLB, latRB, runA, runB, cA, cB, fullPl, lay.W, v0, v1); runOpen = false; }
                        float uL, uR;
                        if (lay.isDefault) GapOf(lay, kn, prevK, out uL, out uR);
                        else { uL = (tex.bandHi + 0.75f) / tw; uR = (tex.bandLo + 0.25f) / tw; }
                        Strip(bk, A, B, latLA, latRA, latLB, latRB, cA, cB, nA, nB, uL, uR, v0, v1); strips++;
                    }
                }
                if (edge) break;
                // the line's own strip, U at the texture's own scale (the
                // painter's m +- half): exactly the texels the old full-width
                // quad showed there, never stretched - from the texture column
                // that carries this paint (an edge's own layout: srcM)
                float lA = spanLines[i].latA, lB = spanLines[i].latB;
                if (ident && !lay.synth[kn] && Mathf.Abs(lay.srcM[kn] - lay.m[kn]) < 1e-3f)
                { if (!runOpen) { runOpen = true; runA = lA - PH; runB = lB - PH; } }
                else
                {
                    if (runOpen) { strips += IdentStrip(bk, A, B, latLA, latRA, latLB, latRB, runA, runB, lA - PH, lB - PH, fullPl, lay.W, v0, v1); runOpen = false; }
                    float uHi = (lay.srcM[kn] + PH) / lay.W, uLo = (lay.srcM[kn] - PH) / lay.W;
                    if (lay.synth[kn])
                    {
                        // a broken line its texture has no broken column for:
                        // the solid column's paint in 10 ft dashes, pavement between
                        float pL = (tex.bandHi + 0.75f) / tw, pR = (tex.bandLo + 0.25f) / tw;
                        strips += StripDashed(bk, A, B, latLA, latRA, latLB, latRB, lA - PH, lB - PH, lA + PH, lB + PH, uHi, uLo, pL, pR, v0, v1);
                    }
                    else { Strip(bk, A, B, latLA, latRA, latLB, latRB, lA - PH, lB - PH, lA + PH, lB + PH, uHi, uLo, v0, v1); strips++; }
                }
                cA = lA + PH; cB = lB + PH; prevK = kn;
            }
            if (runOpen) strips += IdentStrip(bk, A, B, latLA, latRA, latLB, latRB, runA, runB, latRA, latRB, fullPl, lay.W, v0, v1);
            RibbonColumnSpans++; RibbonStrips += strips;
            return strips;
        }

        /// <summary>An identity run's one strip, from (fromA, fromB) to (toA,
        /// toB), at the texture's own U: u = (plus - lat) / W.</summary>
        static int IdentStrip(Bucket bk, in Section A, in Section B, float lLA, float lRA, float lLB, float lRB,
                              float fromA, float fromB, float toA, float toB, float plus, float W, float v0, float v1)
        {
            if (!(toA - fromA > 1e-3f || toB - fromB > 1e-3f)) return 0;
            Strip(bk, A, B, lLA, lRA, lLB, lRB, fromA, fromB, toA, toB, (plus - fromA) / W, (plus - toA) / W, v0, v1);
            return 1;
        }

        /// <summary>Is the texture's m-range (lo, hi) free of paint?</summary>
        static bool PaintFree(LineModel.Layout tex, float lo, float hi)
        {
            if (hi < lo) { float t = lo; lo = hi; hi = t; }
            for (int j = 0; j < tex.m.Length; j++)
                if (tex.m[j] + tex.half > lo + 1e-3f && tex.m[j] - tex.half < hi - 1e-3f) return false;
            return true;
        }

        /// <summary>A broken line cut from a solid column (roads pass L2): the
        /// strip split where the dash phase turns - paint on the first quarter
        /// of each 40 ft repeat (V), as the painter's broken lines are, and the
        /// pavement band between. Returns the quads drawn.</summary>
        static int StripDashed(Bucket bk, in Section A, in Section B, float lLA, float lRA, float lLB, float lRB,
                               float aL, float bL, float aR, float bR, float uLineL, float uLineR, float uPaveL, float uPaveR, float v0, float v1)
        {
            var along = B.P - A.P;
            along = along.sqrMagnitude > 1e-8f ? along.normalized * ColumnOverlapM : Vector2.zero;
            float aS = A.s;
            Vector3 At(in Section c, float lL, float lR, float lat)
            {
                float t = lR - lL > 1e-4f ? Mathf.Clamp01((lat - lL) / (lR - lL)) : 0.5f;
                // roads pass L5: a ramp's end section lies on its HOST's
                // cross-line, not its own - its columns end on that line
                if (c.zoneEnd) return Vector3.Lerp(c.L, c.R, t);
                var q = c.P + c.right * lat + (c.s <= aS ? -along : along);
                float y = Mathf.Lerp(c.L.y, c.R.y, t);
                return new Vector3(q.x - tileOrigin.x, y, q.y - tileOrigin.z);
            }
            Vector3 qAL = At(A, lLA, lRA, aL), qBL = At(B, lLB, lRB, bL), qBR = At(B, lLB, lRB, bR), qAR = At(A, lLA, lRA, aR);
            // the phase breaks between v0 and v1 (either way along)
            float vLo = Mathf.Min(v0, v1), vHi = Mathf.Max(v0, v1);
            breaks.Clear(); breaks.Add(0f);
            if (vHi - vLo > 1e-5f)
                for (float k = Mathf.Floor(vLo); k <= vHi; k += 1f)
                    for (int q = 0; q < 2; q++)
                    {
                        float vb = k + (q == 0 ? 0f : 0.25f);
                        if (vb > vLo + 1e-5f && vb < vHi - 1e-5f) breaks.Add((vb - v0) / (v1 - v0));
                    }
            breaks.Add(1f);
            breaks.Sort();
            int quads = 0;
            for (int i = 0; i + 1 < breaks.Count; i++)
            {
                float ta = breaks[i], tb = breaks[i + 1];
                if (tb - ta < 1e-5f) continue;
                float va = Mathf.Lerp(v0, v1, ta), vb = Mathf.Lerp(v0, v1, tb);
                float vm = 0.5f * (va + vb);
                bool dash = vm - Mathf.Floor(vm) < 0.25f;
                float uL = dash ? uLineL : uPaveL, uR = dash ? uLineR : uPaveR;
                bk.Quad(Vector3.Lerp(qAL, qBL, ta), Vector3.Lerp(qAL, qBL, tb), Vector3.Lerp(qAR, qBR, tb), Vector3.Lerp(qAR, qBR, ta),
                    new Vector2(uL, va), new Vector2(uL, vb), new Vector2(uR, vb), new Vector2(uR, va));
                quads++;
            }
            return quads;
        }
        static readonly List<float> breaks = new List<float>(8);

        /// <summary>The paint-free U band a pavement strip maps to: the texels
        /// between line kn (the line R-ward of the strip; -1 = the R edge) and
        /// the texture's next line on its L side; at the R edge, those between
        /// the L-ward line and the texture's next line R-ward of it. uL at the
        /// strip's L column, uR at its R column.</summary>
        static void GapOf(LineModel.Layout lay, int kn, int kp, out float uL, out float uR)
        {
            float tw = lay.texW;
            int n = lay.m.Length;
            int lo, hi;   // texel range of the gap
            if (kn >= 0) { lo = lay.x1[kn] + 1; hi = kn + 1 < n ? lay.x0[kn + 1] - 1 : lay.texW - 1; }
            else if (kp >= 0) { hi = lay.x0[kp] - 1; lo = kp - 1 >= 0 ? lay.x1[kp - 1] + 1 : 0; }
            else { lo = 0; hi = n > 0 ? lay.x0[0] - 1 : lay.texW - 1; }
            if (hi < lo) hi = lo;
            uL = (hi + 0.75f) / tw; uR = (lo + 0.25f) / tw;
        }

        const float ColumnOverlapM = 0.002f;

        /// <summary>One strip of a span between two laterals on each section.</summary>
        static void Strip(Bucket bk, in Section A, in Section B, float lLA, float lRA, float lLB, float lRB,
                          float aL, float bL, float aR, float bR, float uL, float uR, float v0, float v1)
        {
            // each column span reaches 2 mm past its two sections: the next
            // span may be one quad whose edge the columns' vertices only lie
            // ON (a T-junction), and a ray down exactly there fell through the
            // hairline between them (the drive audit's probes land on section
            // lines). Coplanar, the overlap is never seen.
            var along = B.P - A.P;
            along = along.sqrMagnitude > 1e-8f ? along.normalized * ColumnOverlapM : Vector2.zero;
            float aS = A.s;
            Vector3 At(in Section c, float lL, float lR, float lat)
            {
                float t = lR - lL > 1e-4f ? Mathf.Clamp01((lat - lL) / (lR - lL)) : 0.5f;
                // roads pass L5: a ramp's end section lies on its HOST's
                // cross-line, not its own - its columns end on that line
                if (c.zoneEnd) return Vector3.Lerp(c.L, c.R, t);
                var q = c.P + c.right * lat + (c.s <= aS ? -along : along);
                float y = Mathf.Lerp(c.L.y, c.R.y, t);
                return new Vector3(q.x - tileOrigin.x, y, q.y - tileOrigin.z);
            }
            bk.Quad(At(A, lLA, lRA, aL), At(B, lLB, lRB, bL), At(B, lLB, lRB, bR), At(A, lLA, lRA, aR),
                new Vector2(uL, v0), new Vector2(uL, v1), new Vector2(uR, v1), new Vector2(uR, v0));
        }

        /// <summary>The lines the ribbon draws at one section: the model's, the
        /// edge line on a squeezed side moved in with the edge (a clipped inner
        /// side has none: it runs into its host), and nothing beyond an edge
        /// line or within 2 cm of a drawn edge.</summary>
        static void DrawnLines(CityMap.Edge e, LineModel.Layout lay, in Section c, float eM, float eP, float latL, float latR, List<LineModel.LineAt> into)
        {
            LineModel.LinesAt(e, c.s, into);
            float PH = lay.half;
            bool movedL = latL > -eM + 0.01f, movedR = latR < eP - 0.01f;
            bool clipL = c.clippedIn && c.innerSide < 0, clipR = c.clippedIn && c.innerSide > 0;
            float lo = latL + 0.02f, hi = latR - 0.02f;
            int w = 0;
            for (int i = 0; i < into.Count; i++)
            {
                var ln = into[i];
                byte kind = lay.kind[ln.k];
                if (kind == LineModel.KEdgeM && movedL) { if (clipL) continue; ln.lat = latL + e.shr + RoadProfiles.PaintHalfM; }
                if (kind == LineModel.KEdgeP && movedR) { if (clipR) continue; ln.lat = latR - e.shl - RoadProfiles.PaintHalfM; }
                into[w++] = ln;
            }
            into.RemoveRange(w, into.Count - w);
            // the edge lines bound the rest
            for (int i = 0; i < into.Count; i++)
            {
                byte kind = lay.kind[into[i].k];
                if (kind == LineModel.KEdgeM) lo = Mathf.Max(lo, into[i].lat - PH - 0.01f);
                if (kind == LineModel.KEdgeP) hi = Mathf.Min(hi, into[i].lat + PH + 0.01f);
            }
            w = 0;
            float prev = float.NegativeInfinity;
            for (int i = 0; i < into.Count; i++)
            {
                var ln = into[i];
                if (ln.lat - PH < lo || ln.lat + PH > hi) continue;
                if (ln.lat - PH < prev + 0.01f) continue;   // two lines run into each other: keep the first
                prev = ln.lat + PH;
                into[w++] = ln;
            }
            into.RemoveRange(w, into.Count - w);
        }

        static bool IsThrough(Trims trims, CityMap.Edge e, int node) =>
            trims.throughA[node] == e.index || trims.throughB[node] == e.index;

        /// <summary>The through tangent at a mitred node, in <paramref name="e"/>'s
        /// own direction of travel.</summary>
        static Vector2 MitreAt(CityMap map, Trims trims, CityMap.Edge e, int node, out float widen)
        {
            widen = 1f;
            int oi = trims.throughA[node] == e.index ? trims.throughB[node] : trims.throughA[node];
            var own = e.a == node ? e.TangentAt(0f) : e.TangentAt(e.length);   // e's travel direction at the node
            if (oi < 0 || oi == e.index) return own;
            var o = map.edges[oi];
            // travel through the node: in along e (the reverse of leaving it), out along o
            var m = OutDir(o, node) - OutDir(e, node);
            if (m.sqrMagnitude < 1e-6f) return own;
            m.Normalize();
            if (Vector2.Dot(m, own) < 0f) m = -m;
            return m;
        }

        static IEnumerable<int> BuildRoadsAndDecks(CityMap map, Trims trims, TileMeshes tm,
                                       Vector2 min, Vector2 max)
        {
            segScratch.Clear();
            edgeScratch.Clear();
            map.EdgeSegsInRect(min - Vector2.one * 40f, max + Vector2.one * 40f, segScratch);
            foreach (var packed in segScratch) edgeScratch.Add(packed >> 12);

            if (WarmSections)
                foreach (var wi in edgeScratch)
                {
                    if (rawSectionCache.ContainsKey(wi)) continue;
                    var we = map.edges[wi];
                    if (we.a == we.b && we.length < 1f) continue;
                    float w0s = trims.atA[wi], w1s = we.length - trims.atB[wi];
                    if (w1s - w0s < 0.6f) continue;
                    yield return 0;   // WP-09: an edge's sections a step, ahead of its spans
                    stepPhase = 2; stepItem = wi; stepPart = 4;
                    RawSectionsOf(map, trims, we, w0s, w1s);
                }

            foreach (var ei in edgeScratch)
            {
                yield return 0;   // WP-09: a road segment a step
                stepPhase = 2; stepItem = ei; stepPart = 0;
                var e = map.edges[ei];
                if (e.a == e.b && e.length < 1f) continue;
                float sMin = trims.atA[ei], sMax = e.length - trims.atB[ei];
                if (sMax - sMin < 0.6f) continue;   // the fans own all of it

                // ---- sections ----
                BuildSections(map, trims, tm, e, sMin, sMax);
                int n = sections.Count;
                if (n < 2) continue;
                yield return 0;   // WP-09: the sections, then the side flags, a step each
                if (WarmSections)
                    for (int k = 0; k < n; k++)
                        for (int sd = -1; sd <= 1; sd += 2)
                        {
                            int nb = sections[k].Nb(sd);
                            if (nb < 0 || rawSectionCache.ContainsKey(nb)) continue;
                            var ne = map.edges[nb];
                            if (ne.a == ne.b && ne.length < 1f) continue;
                            float n0s = trims.atA[nb], n1s = ne.length - trims.atB[nb];
                            if (n1s - n0s < 0.6f) continue;
                            stepPart = 5; stepItem = nb;
                            RawSectionsOf(map, trims, ne, n0s, n1s);
                            yield return 0;   // WP-09: a neighbour's sections a step
                        }
                stepItem = ei;
                stepPart = 1;

                // ---- what stands on each side of each span ----
                // Decided for every span within FlagReachM of the tile, so a
                // run that ends just beyond the tile flares and caps the same
                // way whichever tile draws the span beside it.
                foreach (var _ in DecideSideFlagsSteps(map, trims, tm, e, min, max)) yield return 0;
                yield return 0;
                stepPart = 2;

                // ---- spans ----
                float sincePier = PierEvery * 0.6f;
                for (int i = 1; i < n; i++)
                {
                    if ((i & 15) == 0) yield return 0;   // WP-09: sixteen spans a step
                    var A = sections[i - 1]; var B = sections[i];
                    var f = spanFlags[i];
                    if (f.skip) continue;
                    var mid = e.PointAt((A.s + B.s) * 0.5f);
                    bool mine = mid.x >= min.x && mid.x < max.x && mid.y >= min.y && mid.y < max.y;
                    if (!mine) { sincePier += B.s - A.s; continue; }

                    // Per SPAN, not per edge: a road that climbs onto a
                    // viaduct halfway along is asphalt up to the abutment and
                    // concrete over the water.
                    var bk = buckets[(int)RoadSlot(e, f.elev)];
                    // chain distance, not edge s (WP-11): the dash phase runs on through joints
                    float v0 = (e.vOff + e.vDir * A.s) / RoadVTile, v1 = (e.vOff + e.vDir * B.s) / RoadVTile;
                    int firstV = bk.Count;
                    int strips = EmitRibbon(bk, tm, e, A, B, v0, v1);
                    if (tm.tap != null)
                        tm.tap.spans.Add(new RoadTap.Span { slot = (int)RoadSlot(e, f.elev), bucketV = firstV, edge = e.index, sA = A.s, sB = B.s,
                                                             flagsA = TapFlags(A), flagsB = TapFlags(B), strips = strips });

                    if (f.elev)
                    {
                        // plan A2: no fascia on a union side (the slab carries on
                        // across the gap); the partner of a union stands no pier,
                        // its owner a bent under both
                        EmitDeckBox(buckets[(int)Slot.Concrete], A, B, v0, v1, f.l.union, f.r.union);
                        sincePier += B.s - A.s;
                        if (sincePier >= PierEvery)
                        {
                            sincePier = 0f;
                            float sp = (A.s + B.s) * 0.5f;
                            if (UnionOwnerAt(trims, e.index, sp) && !UnionPartnerAt(trims, e.index, sp)) EmitBent(map, trims, e, tm, sp);
                            else if (!UnionPartnerAt(trims, e.index, sp)) EmitPier(map, trims, e, tm, sp);
                        }
                    }
                    for (int side = -1; side <= 1; side += 2)
                        EmitSide(map, trims, tm, e, i, side, v0, v1);
                    if (spanLog != null) RecordSpan(e, A, B, f);   // AuditView's record (audits only)
                }

                // ---- street lamps ----
                // Here, while this edge's sections and side flags are still
                // the ones the tile just drew from: a lamp stands only on a
                // side the tile laid as a plain verge.
                yield return 0;
                stepPart = 3;
                PlaceLamps(map, trims, tm, e, min, max);
            }
        }

        // ------------------------------------------------------------------
        //  Street lamps (2026-09-21, the night pass).
        //
        //  The owner, on taking Need for Speed (2015)'s night for reference:
        //  "how street lights bathe the road". Charlotte had none: a city of
        //  31 km after dark was the headlights and nothing else. Every
        //  carriageway now gets high-pressure sodium heads on davit poles, the
        //  way a US city was lit in 1999, placed per tile from the map alone:
        //
        //    ALONG      evenly over the ribbon between its junction mouths, at
        //               a pitch by road class: a street's 55 m, an arterial's
        //               38 m, a trunk's 42 m and a freeway's 60 m. A four-lane
        //               two-way arterial, and any two-way trunk or freeway,
        //               lights both sides staggered: a lamp every half pitch.
        //               Spread evenly rather than off a hashed phase, the gap
        //               across a node between two edges of one road is about a
        //               pitch, not anything from nothing to two.
        //    SIDE       alternating from a per-edge hash (a street's lamps
        //               zig-zag); the outside only on a one-way trunk or
        //               freeway carriageway and on a roundabout. A lamp refused
        //               on its side tries the other before it gives up.
        //    OFFSET     the owner's DOT rule (the night pass's R11): past
        //               65 km/h a rigid post stands clear of the recoverable
        //               foreslope, at the shoulder plus the 3.5 m clear zone
        //               plus half a metre. A low-speed urban street stands it
        //               a metre past its shoulder, as real downtowns do; it is
        //               also the only way downtown gets lamps at all, with its
        //               buildings a few metres off the drawn edge.
        //    ON         only a side the tile drew as a PLAIN VERGE for three
        //               metres either way. That means no deck, wedge, structure
        //               approach, gore gap, squeeze strip, rail, retaining face,
        //               cut wall or median barrier. No ramps, tunnels or tagged
        //               bridges, and nothing within 20 m of a structure end.
        //    NOT IN     another road's pavement (a metre clear at the foot's
        //               level, six across a divided road's median from its
        //               other carriageway), a junction fan, a building or a
        //               lot, or water. And never under a structure: nothing
        //               passes over the foot, the arm or the head.
        //
        //  OWNERSHIP. A lamp belongs to the tile its STATION (the centreline
        //  point it is placed from) lies in, the rule nodes and footprint
        //  centres already follow, and only that tile evaluates it, nudges and
        //  side fallback included. That is what makes the seams safe: two
        //  tiles never both stand one lamp, and neither drops it, however each
        //  one samples the edge. Owning by SPAN midpoint would be as safe only
        //  while both tiles cut the ribbon into the same spans, and the
        //  per-tile clip table (which adds samples) does not promise that.
        //
        //  The posts are SOLID (CityWorld.Attach: one box per post on the
        //  Solid layer, named LampPost): a car that leaves a street at speed
        //  meets a pole, as it would. That is exactly why the fast roads keep
        //  them out of the clear zone.
        // ------------------------------------------------------------------

        /// <summary>Metres between lamps along one road, by class: a street's
        /// or collector's (0-1), an arterial's (2-3), a trunk's (4), a
        /// freeway's (5). A two-way arterial of four lanes or more, and any
        /// two-way trunk or freeway, lights both sides staggered by half of
        /// this.</summary>
        const float LampPitchStreetM = 55f, LampPitchArterialM = 38f, LampPitchTrunkM = 42f, LampPitchFreewayM = 60f;
        /// <summary>
        /// UPTOWN'S ACORN POSTS (WP-15; the Street View survey, spot A1): black
        /// pedestrian posts 4.5-5 m tall with an acorn globe, every 20-30 m on
        /// BOTH sides of every street inside the freeway loop
        /// (<see cref="CityPoles.IsAcornEdge"/>), and no wires. Placed as
        /// street lamps are (the same verge, pavement, building and structure
        /// rules), a lamp every half pitch on alternating sides; the globe is
        /// the lens (the head stands over the foot, no arm). Drawn black on
        /// the furniture atlas (CityPoles.EmitLamp) once a tile's furniture
        /// stands.
        /// </summary>
        public const byte LampAcorn = 3;
        const float AcornPitchM = 25f, AcornHeightM = 4.7f;
        /// <summary>Foot to lens by the same classes: a 25 ft residential
        /// pole, a 30 ft arterial davit, a 40 ft freeway mast.</summary>
        const float LampHeightStreetM = 7.5f, LampHeightArterialM = 9f, LampHeightFreewayM = 12f;
        /// <summary>Above this speed a post stands clear of the recoverable
        /// foreslope (<see cref="LampClearZonePadM"/> past the clear zone); at
        /// or under it, <see cref="LampUrbanOffsetM"/> past the shoulder.</summary>
        const float LampUrbanSpeedKmh = 65f;
        const float LampUrbanOffsetM = 1.0f;
        const float LampClearZonePadM = 0.5f;
        /// <summary>The arm reaches back over the road by the post's offset
        /// less <see cref="LampArmBackM"/>, never shorter or longer than a
        /// real davit's: the lens lands over the edge or its shoulder.</summary>
        const float LampArmBackM = 0.4f, LampArmMinM = 1.6f, LampArmMaxM = 4.2f;
        /// <summary>How far under the verge surface a post's foot stands, so
        /// a post on a 1V:4H slope shows no daylight under its low corner.</summary>
        public const float LampSinkM = 0.15f;
        /// <summary>Plan clearance from a post's foot to any other road's
        /// pavement (its nominal half width, which a squeeze or clip only
        /// narrows) at the foot's level or above. The audit asserts 0.8.</summary>
        public const float LampPavementClearM = 1.0f;
        /// <summary>The same across a divided road's median, from its OTHER
        /// carriageway: two carriageways' lamps otherwise paired up a metre
        /// apart in every narrow median. A median narrower than this plus the
        /// offset keeps its lamps on the outsides.</summary>
        const float LampMedianClearM = 6f;
        /// <summary>The height band in which another road's pavement is one a
        /// post could stand in: the squeeze's (a car's height and a deck).</summary>
        const float LampBandM = RoadsideRules.CarBandM + CityElevation.DeckThick;
        /// <summary>Plan clearance from a building (a real footprint, a
        /// procedural box or a model's lot) and from water.</summary>
        const float LampBuildingClearM = 0.8f, LampWaterClearM = 1.0f;
        /// <summary>How far a lot's centre may lie from a post and its lot
        /// still reach it (the widest pack tower's half diagonal), and how
        /// far the water is looked for.</summary>
        const float LampLotReachM = 64f, LampWaterReachM = 40f;
        /// <summary>At a junction fan, the first lamp stands back from the
        /// mouth by the widest other arm's half width and this: turning cars
        /// cut the corner, and a pole there is a pole in their path.</summary>
        const float LampFanClearM = 4f;
        /// <summary>The side must be a plain verge this far either way of the
        /// station, so a post never stands beside the first metre of a rail,
        /// a cut wall's shelf or a gore.</summary>
        const float LampRunMarginM = 3f;
        /// <summary>A stretch of ribbon shorter than this between its mouths
        /// takes no lamp at all.</summary>
        const float LampMinRunM = 6f;
        /// <summary>A verge surface further than this above or below the
        /// tarmac under a post is some other bank than the verge the tile laid.</summary>
        const float LampMaxBankM = 1.5f;
        /// <summary>Where a refused station tries next, metres along, never
        /// more than <see cref="LampNudgeShare"/> of the gap to its neighbour.</summary>
        static readonly float[] LampNudges = { 0f, 4f, -4f, 8f, -8f };
        const float LampNudgeShare = 0.3f;
        const int LampSalt = 41;
        /// <summary>Post, arm and head box, metres: a 0.26 m square pole, a
        /// 0.12 m arm, a cobra head 0.75 long, 0.18 deep and 0.40 wide, and the
        /// pole's cap above the arm.</summary>
        const float LampPostW = 0.26f, LampArmW = 0.12f, LampHeadL = 0.75f, LampHeadH = 0.18f, LampHeadW = 0.40f, LampPostCapM = 0.2f;

        /// <summary>Why a lamp station stood no lamp, for the audit (the
        /// reason its first try was refused).</summary>
        public const int LampRejectSide = 1, LampRejectStructure = 2, LampRejectBuilding = 3, LampRejectWater = 4,
                         LampRejectPavement = 5, LampRejectOverhead = 6, LampRejectVerge = 7, LampRejectCount = 8;
        public static readonly string[] LampRejectNames =
        {
            "placed", "not a plain verge", "structure approach", "building or lot", "water",
            "another road's pavement or fan", "under a structure", "no graded verge",
        };
        // A post whose foot lands in a city race route's run-off (RaceRunOff:
        // 8 m past the drawn edge, 16 m on the outside of a bend) stands where
        // it would and BREAKS AWAY (Lamp.breakaway: no collider). The racers
        // ran wide into the lamp posts on Tryon (WP-08's batch: 33 hits).
        // WP-15 first stepped such a post back up to 16 m, else stood none -
        // and uptown, where the buildings stand at the sidewalk, that left
        // N Tryon without one post by day and black at night (the review).

        /// <summary>The speed a lamp's offset is decided by: the edge's posted
        /// limit where OSM tags one, else North Carolina's statutory limit for
        /// its class: 35 mph inside a municipality, 55 on a trunk, 65 on an
        /// interstate.</summary>
        public static float LampSpeedKmh(CityMap.Edge e) =>
            e.speedKmh > 0 ? e.speedKmh : e.cls >= 5 ? 105f : e.cls == 4 ? 89f : 56f;

        /// <summary>Pitch (already halved where both sides are lit), sides,
        /// pole height and lamp kind for one edge.</summary>
        static void LampPlanOf(CityMap map, CityMap.Edge e, out float gap, out bool outsideOnly, out float height, out byte kind)
        {
            if (CityPoles.IsAcornEdge(map, e))
            {
                // both sides, alternating: a post every AcornPitchM on each side
                gap = AcornPitchM * 0.5f; height = AcornHeightM; kind = LampAcorn; outsideOnly = false;
                return;
            }
            if (e.cls >= 5) { gap = LampPitchFreewayM; height = LampHeightFreewayM; kind = 2; }
            else if (e.cls == 4) { gap = LampPitchTrunkM; height = LampHeightArterialM; kind = 1; }
            else if (e.cls >= 2) { gap = LampPitchArterialM; height = LampHeightArterialM; kind = 1; }
            else { gap = LampPitchStreetM; height = LampHeightStreetM; kind = 0; }
            // R is the median of a one-way carriageway (see DecideSideFlags)
            outsideOnly = e.roundabout || (e.oneway && e.cls >= 4);
            bool bothSides = !e.oneway && (e.cls >= 4 || (e.cls >= 2 && e.lanes >= 4));
            if (bothSides) gap *= 0.5f;
        }

        static readonly List<float> lampEnds = new List<float>(8);
        static readonly HashSet<int> lampSegs = new HashSet<int>();
        static readonly HashSet<int> lampNodes = new HashSet<int>();
        static readonly HashSet<int> lampWater = new HashSet<int>();
        static readonly HashSet<int> lampLakes = new HashSet<int>();
        static readonly Vector3[] lampProf = new Vector3[4];
        /// <summary>The building lots of the tile being built (Build's own
        /// argument): the procedural boxes and the models' lots.</summary>
        static Dictionary<long, List<CityBuildings.B>> lampBuildings;

        /// <summary>
        /// Stand this tile's street lamps along one edge. Called from the span
        /// loop of <see cref="BuildRoadsAndDecks"/>, so <see cref="sections"/>
        /// and <see cref="spanFlags"/> are this edge's, exactly as the tile
        /// drew them. Deterministic from the map, the trims and the tile.
        /// </summary>
        static void PlaceLamps(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, Vector2 min, Vector2 max)
        {
            if (e.link || e.tunnel || e.bridge) return;
            // WP-15: a road with a pole line is lit by the cobra-heads on its
            // poles (CityPoles); it stands no lamp posts of its own
            if (CityPoles.Enabled && CityPoles.IsPoleEdge(map, e)) return;
            int n = sections.Count;
            if (n < 2) { LampTrace?.Invoke(e.index, -2f, -1); return; }
            if (lampTrims != trims || lampFanReach == null || lampFanReach.Length != map.nodes.Length)
            {
                // per map (the trims are): fan reaches fill in as nodes are met
                lampTrims = trims;
                lampFanReach = new float[map.nodes.Length];
                for (int k = 0; k < lampFanReach.Length; k++) lampFanReach[k] = -1f;
            }
            LampPlanOf(map, e, out float gap, out bool outsideOnly, out float height, out byte kind);
            float lo = sections[0].s + LampEndClear(map, trims, e, e.a);
            float hi = sections[n - 1].s - LampEndClear(map, trims, e, e.b);
            float run = hi - lo;
            if (run < LampMinRunM) { LampTrace?.Invoke(e.index, -1f, -1); return; }
            int count = Mathf.Max(1, Mathf.RoundToInt(run / gap));
            float step = run / count;
            int phase = Hash01(e.index, 1, LampSalt) < 0.5f ? 0 : 1;
            float speed = LampSpeedKmh(e);
            StructureEnds(map, trims, e, lampEnds);
            for (int k = 0; k < count; k++)
            {
                float s0 = lo + (k + 0.5f) * step;
                var p0 = e.PointAt(s0);
                if (p0.x < min.x || p0.x >= max.x || p0.y < min.y || p0.y >= max.y) continue;   // another tile's lamp
                tm.lampStations++;
                int first = outsideOnly ? -1 : ((k + phase) & 1) == 0 ? -1 : 1;
                int why = -1;
                bool placed = false;
                for (int attempt = 0; attempt < (outsideOnly ? 1 : 2) && !placed; attempt++)
                {
                    int side = attempt == 0 ? first : -first;
                    foreach (float dS in LampNudges)
                    {
                        if (Mathf.Abs(dS) > LampNudgeShare * step) continue;
                        float s = s0 + dS;
                        if (s < lo || s > hi) continue;
                        int r = TryLamp(map, trims, tm, e, s, side, height, kind, speed);
                        if (why < 0) why = r;
                        if (r == 0) { placed = true; break; }
                    }
                }
                if (!placed && why > 0) tm.lampRejects[why]++;
                LampTrace?.Invoke(e.index, s0, placed ? 0 : why);
            }
        }

        /// <summary>The audits' look at why a street has no lamp where it has
        /// none: (edge, station arc, 0 placed or its first try's
        /// <see cref="LampRejectNames"/> index) for every station, when set;
        /// (edge, -1, -1) for an edge shorter than its junctions' fans leave
        /// room for, (edge, -2, -1) for one the tile drew no span of.</summary>
        public static System.Action<int, float, int> LampTrace;

        /// <summary>How far from a node's end of the ribbon the first lamp
        /// stands back: clear of a junction fan's mouth, nothing at a mitred
        /// node the road carries on through.</summary>
        static float LampEndClear(CityMap map, Trims trims, CityMap.Edge e, int node)
        {
            if (!trims.patch[node]) return 0f;
            float hw = 0f;
            foreach (int oi in map.nodeEdges[node])
                if (oi != e.index) hw = Mathf.Max(hw, map.edges[oi].HalfMax);
            return hw + LampFanClearM;
        }

        /// <summary>One lamp at arc <paramref name="s"/> on one side, or the
        /// reason it cannot stand there (0 = placed).</summary>
        static int TryLamp(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, float s, int side,
                           float height, byte kind, float speed)
        {
            if (!LampSideClear(side, s - LampRunMarginM, s + LampRunMarginM)) return LampRejectSide;
            foreach (float se in lampEnds)
                if (Mathf.Abs(s - se) < ApproachRailM) return LampRejectStructure;

            // the drawn edge at the station: interpolated between the two
            // sections round it, as EmitStrip lays the verge from them
            int n = sections.Count, i = 1, hiI = n - 1;
            while (i < hiI) { int mid = (i + hiI) >> 1; if (sections[mid].s < s) i = mid + 1; else hiI = mid; }
            var A = sections[i - 1]; var B = sections[i];
            float t = Mathf.Clamp01((s - A.s) / Mathf.Max(B.s - A.s, 1e-4f));
            var ed = Vector3.Lerp(A.Edge(side), B.Edge(side), t) + tm.origin;
            var outw = Vector2.Lerp(A.Out(side), B.Out(side), t);
            if (outw.sqrMagnitude < 1e-6f) return LampRejectSide;
            outw.Normalize();
            var edgeW = new Vector2(ed.x, ed.z);

            float shoulder = ShoulderOf(e, side);
            float off = speed > LampUrbanSpeedKmh
                ? shoulder + RoadsideRules.ClearZoneM + LampClearZonePadM
                : shoulder + LampUrbanOffsetM;
            float arm = kind == LampAcorn ? 0f : Mathf.Clamp(off - LampArmBackM, LampArmMinM, LampArmMaxM);
            var footP = edgeW + outw * off;
            var headP = footP - outw * arm;

            // cheapest first. The real buildings are asked with FootprintClear,
            // NOT AnyFootprintNear: that one opens only the bucket of a
            // footprint's CENTRE and measures to corners, so the review found
            // posts inside buildings a tile seam away and flush against the
            // middle of long facades downtown (West 6th, West 4th, East 7th,
            // West 3rd). FootprintClear opens every bucket a building could
            // reach from and measures to the walls.
            if (!map.FootprintClear(footP, LampBuildingClearM) || LampInLot(footP) || OnDriveway(footP, 0.8f)) return LampRejectBuilding;
            if (LampInWater(map, footP)) return LampRejectWater;
            int clash = LampPavementClear(map, trims, e, side, s, footP, headP, ed.y);
            if (clash != 0) return clash;

            // THE VERGE IT STANDS ON, solved as the tile lays it: a post on the
            // lattice where the verge has already tucked under it, on the
            // verge's own face where that is the higher of the two.
            GatherNear(map, edgeW, edgeW, VergeMaxRunM + RoadsideRules.ToeTuckRunM);
            var sh = new StripShape { shoulder = shoulder, maxRun = VergeMaxRunM };
            if (!SolveStrip(map, trims, edgeW, ed.y, outw, sh, lampProf, out bool graded, out _) || !graded) return LampRejectVerge;
            float ground = LampGroundAt(map, lampProf, edgeW, off, footP);
            if (Mathf.Abs(ground - ed.y) > LampMaxBankM) return LampRejectVerge;

            var foot = new Vector3(footP.x, ground - LampSinkM, footP.y) - tm.origin;
            var head = new Vector3(headP.x, ground - LampSinkM + height, headP.y) - tm.origin;
            // in a city race route's run-off it breaks away (the WP-15 review)
            bool breakaway = RaceRunOff.Inside(map, trims, footP);
            tm.lamps.Add(new Lamp { foot = foot, head = head, height = height, kind = kind, breakaway = breakaway });
            if (kind == LampAcorn) EmitAcorn(foot, head);
            else EmitLamp(foot, head, outw);
            return 0;
        }

        /// <summary>Is this side a plain verge over every span touching
        /// [s0, s1]? The same flags EmitSide drew it from.</summary>
        static bool LampSideClear(int side, float s0, float s1)
        {
            for (int i = 1; i < sections.Count; i++)
            {
                var A = sections[i - 1]; var B = sections[i];
                if (B.s <= s0) continue;
                if (A.s >= s1) break;
                var f = spanFlags[i]; var sf = f[side];
                if (f.skip || f.elev || f.wedge || f.approach || !f.decided) return false;
                if (sf.gap || sf.rail || sf.retain || sf.cut || sf.median || sf.union) return false;
                if (A.collapsed || B.collapsed || A.Strip(side) >= 0f || B.Strip(side) >= 0f) return false;
                if (SpanOnWBeam(side, i)) return false;   // leftover item 2: not on a W-beam lead-in
            }
            return true;
        }

        /// <summary>
        /// Does any road stand where a post would: another pavement within
        /// <see cref="LampPavementClearM"/> of the foot at its level or above
        /// it (our own included, for the inside of a tight bend), a junction
        /// fan within its reach, or any surface over the arm or the head? The
        /// arm is ALLOWED over roads at its own level (reaching over the lanes
        /// is what it is for); only something a car's height above the tarmac
        /// is overhead. Returns a LampReject code, or 0.
        /// </summary>
        static int LampPavementClear(CityMap map, Trims trims, CityMap.Edge e, int side, float s,
                                     Vector2 foot, Vector2 head, float yRef)
        {
            lampSegs.Clear(); lampNodes.Clear();
            float r = CityElevation.MaxCorridorHalf + LampMedianClearM;
            map.EdgeSegsInRect(Vector2.Min(foot, head) - Vector2.one * r, Vector2.Max(foot, head) + Vector2.one * r, lampSegs);
            var armMid = (foot + head) * 0.5f;
            var tanE = e.TangentAt(s);
            foreach (int packed in lampSegs)
            {
                int oi = packed >> 12, si = packed & 0xFFF;
                var o = map.edges[oi];
                lampNodes.Add(o.a); lampNodes.Add(o.b);
                Vector2 a = o.pts[si], d = o.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                if (L2 < 1e-6f) continue;
                float len = Mathf.Sqrt(L2);

                float tf = Mathf.Clamp01(Vector2.Dot(foot - a, d) / L2);
                float atF = o.s[si] + len * tf;
                float yF = o.YAt(atF);
                float need = LampPavementClearM;
                // the other carriageway of a divided road, across the median
                if (oi != e.index && side > 0 && e.oneway && o.oneway && Vector2.Dot(d / len, tanE) < -0.7f)
                    need = LampMedianClearM;
                if (Vector2.Distance(foot, a + d * tf) - trims.ReachAt(o, atF) < need && yF > yRef - LampBandM)
                    return yF > yRef + RoadsideRules.CarBandM ? LampRejectOverhead : LampRejectPavement;

                for (int q = 0; q < 2; q++)
                {
                    var p = q == 0 ? armMid : head;
                    float tq = Mathf.Clamp01(Vector2.Dot(p - a, d) / L2);
                    float atQ = o.s[si] + len * tq;
                    if (Vector2.Distance(p, a + d * tq) - trims.ReachAt(o, atQ) < LampPavementClearM &&
                        o.YAt(atQ) > yRef + RoadsideRules.CarBandM)
                        return LampRejectOverhead;
                }
            }
            // A fan's corners stand at its arms' trims, outside the arms'
            // bands where they meet: the disc through its farthest corner
            // stands for its pavement. Our own junctions' fans are asked too;
            // on a straight edge LampEndClear has already put every station
            // past them, and a curving one is kept off them here.
            foreach (int nd in lampNodes)
            {
                if (!trims.patch[nd]) continue;
                float reach = LampFanReach(map, trims, nd) + LampPavementClearM;
                if ((map.nodes[nd] - foot).sqrMagnitude < reach * reach && map.nodeY[nd] > yRef - LampBandM)
                    return map.nodeY[nd] > yRef + RoadsideRules.CarBandM ? LampRejectOverhead : LampRejectPavement;
            }
            return 0;
        }

        /// <summary>Per node, <see cref="LampFanReach"/> once asked (-1 until
        /// then), for the trims it was measured against.</summary>
        static float[] lampFanReach;
        static Trims lampTrims;

        /// <summary>The distance from a fan node to its farthest arm corner
        /// (an arm's trim along it, its half width across), once per node.</summary>
        static float LampFanReach(CityMap map, Trims trims, int node)
        {
            float r = lampFanReach[node];
            if (r >= 0f) return r;
            r = 0f;
            var cl = trims.ClusterOfNode(node);
            if (cl == null)
                foreach (int oi in map.nodeEdges[node])
                {
                    var o = map.edges[oi];
                    float tr = trims.TrimAt(o, node), hw = o.HalfMax;
                    r = Mathf.Max(r, Mathf.Sqrt(tr * tr + hw * hw));
                }
            else
                // a junction cluster (plan A11): to every member's outside arms' corners
                foreach (int m in cl.nodes)
                    foreach (int oi in map.nodeEdges[m])
                    {
                        var o = map.edges[oi];
                        if (o.a == o.b || trims.Internal(oi)) continue;
                        float tr = trims.TrimAt(o, m);
                        var p = o.PointAt(o.a == m ? Mathf.Min(tr, o.length) : Mathf.Max(0f, o.length - tr));
                        r = Mathf.Max(r, Vector2.Distance(map.nodes[node], p) + o.HalfMax);
                    }
            lampFanReach[node] = r;
            return r;
        }

        /// <summary>Is the point on (or within <see cref="LampBuildingClearM"/>
        /// of) a procedural building or a model's lot? The real footprints are
        /// CityMap.FootprintClear's.</summary>
        static bool LampInLot(Vector2 p)
        {
            if (lampBuildings == null) return false;
            int x0 = Mathf.FloorToInt((p.x - LampLotReachM) / TileSize), x1 = Mathf.FloorToInt((p.x + LampLotReachM) / TileSize);
            int z0 = Mathf.FloorToInt((p.y - LampLotReachM) / TileSize), z1 = Mathf.FloorToInt((p.y + LampLotReachM) / TileSize);
            for (int tz = z0; tz <= z1; tz++)
                for (int tx = x0; tx <= x1; tx++)
                {
                    // CityBuildings' own bucket key
                    if (!lampBuildings.TryGetValue(((long)tx << 24) ^ (tz & 0xFFFFFF), out var list)) continue;
                    foreach (var b in list)
                    {
                        var q = p - b.pos;
                        float reach = 0.5f * (b.w + b.d) + LampBuildingClearM;
                        if (q.sqrMagnitude > reach * reach) continue;
                        float cy = Mathf.Cos(b.yaw), sy = Mathf.Sin(b.yaw);
                        // rgt = (cy, -sy) carries the width, fwd = (sy, cy) the depth (BuildBuildings)
                        if (Mathf.Abs(q.x * cy - q.y * sy) < b.w * 0.5f + LampBuildingClearM &&
                            Mathf.Abs(q.x * sy + q.y * cy) < b.d * 0.5f + LampBuildingClearM) return true;
                    }
                }
            return false;
        }

        /// <summary>Is the point in a river (its drawn width and a margin) or
        /// inside a lake?</summary>
        static bool LampInWater(CityMap map, Vector2 p)
        {
            lampWater.Clear(); lampLakes.Clear();
            map.WaterSegsInRect(p - Vector2.one * LampWaterReachM, p + Vector2.one * LampWaterReachM, lampWater);
            foreach (int packed in lampWater)
            {
                int wi = packed >> 12, si = packed & 0xFFF;
                var w = map.waters[wi];
                if (w.lake) continue;   // the inside is asked below

                if (si + 1 >= w.pts.Length) continue;
                Vector2 a = w.pts[si], d = w.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                if (Vector2.Distance(p, a + d * t) < w.width * 0.5f + LampWaterClearM) return true;
            }
            return map.InLake(p);
        }

        /// <summary>The ground a post stands on <paramref name="d"/> metres out
        /// from the edge: the solved verge profile (world space, its points
        /// measured from <paramref name="edgeW"/>) where it is drawn, and the
        /// lattice where that is higher or the verge has tucked under it.</summary>
        static float LampGroundAt(CityMap map, Vector3[] prof, Vector2 edgeW, float d, Vector2 at)
        {
            float lat = LatticeY(map, at.x, at.y);
            float prevE = 0f, prevY = prof[0].y;
            for (int q = 1; q < 4; q++)
            {
                float eq = Vector2.Distance(new Vector2(prof[q].x, prof[q].z), edgeW);
                if (d <= eq)
                {
                    float y = eq - prevE > 1e-4f ? Mathf.Lerp(prevY, prof[q].y, (d - prevE) / (eq - prevE)) : prof[q].y;
                    return Mathf.Max(y, lat);
                }
                prevE = eq; prevY = prof[q].y;
            }
            return lat;
        }

        /// <summary>Faces of a lamp box <see cref="EmitLampBox"/> leaves out,
        /// as bits in its (+x, -x, +y, -y, +z, -z) order: a post's buried
        /// bottom, an arm's two ends inside the post and the head.</summary>
        const int LampSkipBottom = 1 << 3, LampSkipEnds = (1 << 0) | (1 << 1);

        /// <summary>A post, its arm and its head into the lamp bucket
        /// (tile-local): the arm runs from the post's axis, at the head box's
        /// mid-height, back to the head.</summary>
        static void EmitLamp(Vector3 foot, Vector3 head, Vector2 outw)
        {
            var up = Vector3.up;
            var tw = new Vector3(-outw.x, 0f, -outw.y);   // toward the road
            var ac = new Vector3(-tw.z, 0f, tw.x);          // across the arm
            float armY = head.y + LampHeadH * 0.5f;
            float top = armY + LampArmW * 0.5f + LampPostCapM;
            float hp = LampPostW * 0.5f;
            EmitLampBox(new Vector3(foot.x, (foot.y + top) * 0.5f, foot.z), tw * hp, up * ((top - foot.y) * 0.5f), ac * hp, LampSkipBottom);
            var box = new Vector3(head.x, armY, head.z);
            var from = new Vector3(foot.x, armY, foot.z);
            var to = box - tw * (LampHeadL * 0.5f);
            float half = Vector3.Distance(from, to) * 0.5f;
            if (half > 0.01f)
                EmitLampBox((from + to) * 0.5f, tw * half, up * (LampArmW * 0.5f), ac * (LampArmW * 0.5f), LampSkipEnds);
            EmitLampBox(box, tw * (LampHeadL * 0.5f), up * (LampHeadH * 0.5f), ac * (LampHeadW * 0.5f), 0);
        }

        /// <summary>An acorn post (WP-15) into the lamp bucket: a post and the
        /// globe over it. The furniture mesh draws it black once the tile's
        /// furniture stands (CityPoles); this is the lamp mesh's own copy.</summary>
        static void EmitAcorn(Vector3 foot, Vector3 head)
        {
            const float post = 0.14f, globeW = 0.36f, globeH = 0.46f;
            float topPost = head.y - globeH * 0.5f;
            EmitLampBox(new Vector3(foot.x, (foot.y + topPost) * 0.5f, foot.z), Vector3.right * (post * 0.5f),
                        Vector3.up * ((topPost - foot.y) * 0.5f), Vector3.forward * (post * 0.5f), LampSkipBottom);
            EmitLampBox(head, Vector3.right * (globeW * 0.5f), Vector3.up * (globeH * 0.5f), Vector3.forward * (globeW * 0.5f), 0);
        }

        /// <summary>A box from its centre and three half-axis vectors, every
        /// face facing out (Bucket.Face decides the winding), less the faces
        /// in <paramref name="skip"/>. Four vertices a face, so the normals
        /// come out flat. UVs in metres (WP-07): the posts wear the owner's
        /// pack metal now, one repeat a metre, where they were a flat tint.</summary>
        static void EmitLampBox(Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, int skip)
        {
            for (int f = 0; f < 6; f++)
            {
                if ((skip & (1 << f)) != 0) continue;
                Vector3 nrm = f < 2 ? ax : f < 4 ? ay : az;
                Vector3 u = f < 2 ? ay : ax;
                Vector3 v = f < 4 ? az : ay;
                if ((f & 1) != 0) nrm = -nrm;
                var q = c + nrm;
                float lu = 2f * u.magnitude, lv = 2f * v.magnitude;
                lampBucket.Face(q + u + v, q + u - v, q - u - v, q - u + v, nrm,
                    new Vector2(lu, lv), new Vector2(lu, 0f), Vector2.zero, new Vector2(0f, lv));
            }
        }

        // ------------------------------------------------------------------
        //  One side of one span: what the car meets past the drawn edge.
        //
        //  The owner, 2026-09-13: "Roads sitting cm above the ground do not
        //  need rails/walls, they should meet the ground properly by DOT
        //  standards." So a grounded edge gets a VERGE — flush, an inch down
        //  (RoadsideRules.EdgeDropM), a shoulder, then 1V:6H until it crosses
        //  the lattice with its toe tucked under — and a barrier only where
        //  one is warranted:
        //
        //    RAIL      every span side on structure; every grounded span
        //              within ApproachRailM of a structure end; every grounded
        //              span whose verge cannot grade the land beside it, or
        //              whose finished fall within RoadsideRules.WarrantReachM
        //              is critical (DropFrom). Solid, 0.6 m deep, on the Solid
        //              layer, capped where its run ends. A grounded rail
        //              stands over the full graded verge; where the verge
        //              cannot grade the land it stands on a RETAINING face
        //              instead, and a run on such a face — or on a deck —
        //              never flares or eases off the edge (there is nothing
        //              beside the edge to stand on). A rail over a verge
        //              flares at a grounded run end and eases out flush where
        //              it takes over from a barrier standing at the edge.
        //    JERSEY    a freeway's median side (a real one has it).
        //    RETAINING a freeway's outside in a cut, decided per RUN, with
        //              the ground behind it held at its top.
        // ------------------------------------------------------------------

        struct SideFlags
        {
            /// <summary>Another surface owns this edge: a gore, a clipped
            /// branch's inner side, a zero-width wedge.</summary>
            public bool gap;
            public bool rail, retain, cut, median;
            /// <summary>Why <see cref="cut"/> (WP-14, for the audit): 1 the
            /// back slope does not reach the land, 2 a road above stands in
            /// it, 3 a building does; 0 a run closed over a gap.</summary>
            public byte cutWhy;
            /// <summary>This span starts / ends its side's run of one kind of
            /// barrier (rail, retaining wall, median barrier).</summary>
            public bool capStart, capEnd;
            /// <summary>Plan A2: a twin-deck union's inner side - one structure
            /// with the road beside it (<see cref="UnionRun"/>). No rail, Jersey,
            /// cut wall, verge or fascia: its owner draws the median.</summary>
            public bool union;
        }
        struct SpanFlags
        {
            /// <summary>decided: inside the tile's flag window, where the
            /// ground is probed. approach: within ApproachRailM of a structure end.</summary>
            public bool elev, wedge, skip, decided, approach;
            public SideFlags l, r;
            public SideFlags this[int side] { get => side < 0 ? l : r; set { if (side < 0) l = value; else r = value; } }
        }
        static readonly List<SpanFlags> spanFlags = new List<SpanFlags>(64);
        static readonly List<float> flareL = new List<float>(64), flareR = new List<float>(64);
        /// <summary>A RAIL's own outward ease where it takes over from a
        /// barrier standing at the edge (see the run-end loop); the barrier
        /// beside it keeps its line.</summary>
        static readonly List<float> shiftL = new List<float>(64), shiftR = new List<float>(64);
        /// <summary>A barrier's height at each section, 0..1 of full (the
        /// sloped ends, <see cref="TaperLenM"/>); 1 everywhere else.</summary>
        static readonly List<float> taperL = new List<float>(64), taperR = new List<float>(64);
        /// <summary>Leftover item 2: a W-beam lead-in's arc distance from its
        /// parapet end at each section it reaches (NaN elsewhere) - one list
        /// for the lead-ins running up the edge's sections (U) and one for
        /// those running down (D), so two facing ones never share a value.</summary>
        static readonly List<float> wbUL = new List<float>(64), wbUR = new List<float>(64),
                                    wbDL = new List<float>(64), wbDR = new List<float>(64);
        static List<float> WbList(int side, int dir) => side < 0 ? (dir > 0 ? wbUL : wbDL) : (dir > 0 ? wbUR : wbDR);
        static bool OnWBeam(int side, int k) =>
            !float.IsNaN((side < 0 ? wbUL : wbUR)[k]) || !float.IsNaN((side < 0 ? wbDL : wbDR)[k]);
        static bool SpanOnWBeam(int side, int i) =>
            (!float.IsNaN(WbList(side, 1)[i - 1]) && !float.IsNaN(WbList(side, 1)[i])) ||
            (!float.IsNaN(WbList(side, -1)[i - 1]) && !float.IsNaN(WbList(side, -1)[i]));
        static readonly List<(int k, int dir, int how, float room, int jEnd)> endPlan = new List<(int, int, int, float, int)>(8);
        const int EndInside = 0, EndSloped = 1, EndWBeam = 2, EndConnect = 3;
        /// <summary>The shortest open roadside a rail that guards a drop to its
        /// very end is sloped down over (steeper than TaperLenM's 1:10, but no
        /// square face); with less it keeps its square end.</summary>
        const float ShortSlopeM = 1.5f;
        static readonly List<float> endScratch = new List<float>(8);

        /// <summary>The arc positions on an edge where it goes onto or off
        /// structure: inside the edge where ElevatedAt changes, at a node end
        /// where this edge is on the ground and the road it continues is on
        /// structure there, and — past either end, as an arc below 0 or above
        /// the length — where the road it continues goes on or off structure
        /// within ApproachRailM of the node. Without that last kind an
        /// approach run whose structure ended ten metres into the next OSM
        /// way stopped at the node with a blunt end, ten metres short.</summary>
        static void StructureEnds(CityMap map, Trims trims, CityMap.Edge e, List<float> ends)
        {
            ends.Clear();
            if (e.stS.Length < 2) return;
            InternalStructureEnds(e, ends);
            for (int end = 0; end < 2; end++)
            {
                int node = end == 0 ? e.a : e.b;
                bool eOn = e.ElevatedAt(end == 0 ? 0f : e.length);
                var o = ThroughPartner(map, e, node);
                if (o == null || o.stS.Length < 2) continue;
                bool fromA = o.a == node;
                if (!eOn && o.ElevatedAt(fromA ? 0f : o.length)) { ends.Add(end == 0 ? 0f : e.length); continue; }
                partnerEnds.Clear();
                InternalStructureEnds(o, partnerEnds);
                foreach (float so in partnerEnds)
                {
                    float d = fromA ? so : o.length - so;
                    if (d > 0f && d < ApproachRailM) ends.Add(end == 0 ? -d : e.length + d);
                }
            }
        }

        static readonly List<float> partnerEnds = new List<float>(4);

        static void InternalStructureEnds(CityMap.Edge e, List<float> ends)
        {
            int n = e.stS.Length;
            bool prev = e.stElev[0] || e.stElev[1];
            for (int i = 1; i + 1 < n; i++)
            {
                bool on = e.stElev[i] || e.stElev[i + 1];
                if (on != prev) ends.Add(e.stS[i]);
                prev = on;
            }
        }

        /// <summary>The arm at a node that most nearly continues an edge.</summary>
        static CityMap.Edge ThroughPartner(CityMap map, CityMap.Edge e, int node)
        {
            var dOut = OutDir(e, node);
            CityMap.Edge best = null; float bd = ThroughCos;
            foreach (var oi in map.nodeEdges[node])
            {
                var o = map.edges[oi];
                if (o == e || o.a == o.b) continue;
                float d = Vector2.Dot(dOut, OutDir(o, node));
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        static readonly Dictionary<int, bool> fanStructure = new Dictionary<int, bool>();
        /// <summary>A junction fan on structure: an arm on structure at its
        /// trim, or the node standing more than a metre over its ground.</summary>
        static bool FanOnStructure(CityMap map, Trims trims, int node)
        {
            if (fanStructure.TryGetValue(node, out bool on)) return on;
            // a junction cluster (plan A11): any member's arm, or any member
            var cl = trims.ClusterOfNode(node);
            var members = cl != null ? cl.nodes : null;
            int count = members != null ? members.Length : 1;
            for (int k = 0; k < count && !on; k++)
            {
                int m = members != null ? members[k] : node;
                foreach (var ei in map.nodeEdges[m])
                {
                    var e = map.edges[ei];
                    if (e.a == e.b || trims.Internal(ei)) continue;
                    if (ArmElevatedAtTrim(map, trims, e, m)) { on = true; break; }
                }
                var np = map.nodes[m];
                if (!on) on = map.nodeY[m] - CityElevation.GroundY(map, np.x, np.y) > 1f;
            }
            fanStructure[node] = on;
            return on;
        }

        static bool ArmElevatedAtTrim(CityMap map, Trims trims, CityMap.Edge e, int node)
        {
            float trim = trims.TrimAt(e, node);
            return e.ElevatedAt(e.a == node ? trim : e.length - trim);
        }

        /// <summary>
        /// Is a fan's chord between two arm corners a warranted barrier? When
        /// either arm beside it is on structure at its trim — the chord is a
        /// deck edge — or the land beyond it is a drop a verge cannot grade,
        /// by the test a ribbon's side takes (<see cref="DropAt"/>) but for
        /// its ledge rule (see below).
        /// Railing EVERY chord of a fan with a deck arm stood a rail across
        /// the level corner opposite the bridge at every bridge-end T: a
        /// barrier on a graded verge, which is what the owner ruled out.
        /// Corners are relative to <paramref name="origin"/>.
        /// </summary>
        static bool ChordRailed(CityMap map, Trims trims, int node, FanCorner k0, FanCorner k1, Vector3 origin)
        {
            bool onDeck = ArmElevatedAtTrim(map, trims, map.edges[k0.edge], k0.node) || ArmElevatedAtTrim(map, trims, map.edges[k1.edge], k1.node);
            var a = new Vector2(k0.pos.x + origin.x, k0.pos.z + origin.z);
            var b = new Vector2(k1.pos.x + origin.x, k1.pos.z + origin.z);
            var chord = b - a;
            float len = chord.magnitude;
            if (len < 0.05f) return onDeck;
            // The perimeter runs anticlockwise (FanCorners): outward is the
            // chord's right, the side BuildJunctions stands the rail and lays
            // the verge on and the rail census probes. "Away from the node"
            // is the same side only on a star-shaped ring; on one cut into
            // ears a chord facing back into a notch probed the fan itself.
            var nrm = new Vector2(chord.y, -chord.x) / len;
            if (!onDeck)
            {
                // a quarter metre in from each corner, and no more than 2.5 m apart
                // between (a chord at an oblique fan runs to 21 m)
                //
                // Not by the LEDGE rule (Ungraded's ledges false): a chord's
                // verge stopped a pad short of a road a level below is at a
                // junction's mouth, never beside a road running past — a
                // harness census of every fan city-wide found 13 chords that
                // rule railed, and every one stepped onto another junction's
                // fan or an arm within 5 m of its trim (2026-09-14). Traffic
                // turns across that line, and 4 of the 13 rails stood in a lane
                // mouth (node 6824's chord in e9986's at node 6825, node 7867's
                // in Charleston Drive's at node 4557, nodes 2638 and 7165 at
                // their own arms' corners). Two junctions drawn into each other
                // a level apart are the fans' to seat or split, as a road drawn
                // into another is the squeeze's and the clip's, not a barrier's.
                float inset = Mathf.Min(0.5f, 0.25f / len);
                int probes = Mathf.Max(2, Mathf.CeilToInt(len / VergeStepM) + 1);
                bool warranted = false;
                for (int q = 0; q < probes && !warranted; q++)
                {
                    float t = Mathf.Lerp(inset, 1f - inset, (float)q / (probes - 1));
                    warranted = (DropFrom(map, trims, Vector2.Lerp(a, b, t), nrm, Mathf.Lerp(k0.pos.y, k1.pos.y, t), VergeShoulderM, false) & DropWarrant) != 0;
                }
                // ...and at the corners themselves, which the rail census walks
                // too. Inset probes on a chord a third of a metre long read only
                // its middle, and the verge from node 5211's high corner (e9968's,
                // 1.2 m over e7753's a third of a metre away) ran its whole 8 m
                // without grading, 1.95 m over the land 5 m out: a drop left open
                // once the corner fill that had lain over it as a plate was taken
                // out (CornerFill). City-wide this rails that chord and node 729's
                // (1.6 m), 4 m in all.
                for (int q = 0; q < 2 && !warranted; q++)
                    warranted = (DropFrom(map, trims, q == 0 ? a : b, nrm, q == 0 ? k0.pos.y : k1.pos.y, VergeShoulderM, false) & DropWarrant) != 0;
                if (!warranted) return false;
            }
            // A chord with pavement beyond it at its own height is no edge:
            // where junctions a metre or two apart on a bridge share their
            // stub arms, each fan's chord lies inside the next fan, and its
            // rail stood across that fan's lane mouths (the Freedom Drive
            // cluster at nodes 363, 458, 4106 and 13286; 2026-09-14).
            return !PavedOnward(map, trims, a, b, k0.pos.y - FanProudM, k1.pos.y - FanProudM, nrm, node);
        }

        static readonly List<FanCorner> railCornerScratch = new List<FanCorner>(12);

        /// <summary>How far past a fan chord or gore nose the pavement must
        /// carry on, and how close to its height, for the line to be no edge
        /// at all: a rail's width, and the roadside audit's own walk out over
        /// flush pavement (RailProbe's decimetre).</summary>
        const float PavedOnwardM = RailW, PavedOnwardDyM = 0.1f;
        /// <summary>The most PavedOnward's samples lie apart along the line:
        /// the shortest open run the rail census fails
        /// (RoadsideRules.DeckRailGapFailM). At a verge step's 2.5 m, the wedge
        /// between two arms of the next fan could lie between two samples
        /// that each landed on an arm, and the rail stood down over it.</summary>
        const float PavedOnwardStepM = RoadsideRules.DeckRailGapFailM;

        /// <summary>
        /// Does drawn pavement — a ribbon as drawn or a junction fan other than
        /// <paramref name="skipNode"/>'s — carry on past the line a-b, along
        /// its whole length, within <see cref="PavedOnwardDyM"/> of its height?
        /// Sampled no more than <see cref="PavedOnwardStepM"/> apart, a quarter
        /// metre in from each end, <see cref="PavedOnwardM"/> out along
        /// <paramref name="outw"/>. Asked only of a line already warranted a
        /// rail, and most stop at the first sample, over open land.
        /// </summary>
        static bool PavedOnward(CityMap map, Trims trims, Vector2 a, Vector2 b, float ya, float yb, Vector2 outw, int skipNode)
        {
            float len = Vector2.Distance(a, b);
            if (len < 0.05f) return false;
            GatherNear(map, a, b, PavedOnwardM + 0.5f);
            GatherPavement(map, trims);
            float inset = Mathf.Min(0.5f, 0.25f / len);
            int probes = Mathf.Max(2, Mathf.CeilToInt(len / PavedOnwardStepM) + 1);
            for (int q = 0; q < probes; q++)
            {
                float t = Mathf.Lerp(inset, 1f - inset, (float)q / (probes - 1));
                if (!PavementAt(map, trims, Vector2.Lerp(a, b, t) + outw * PavedOnwardM, Mathf.Lerp(ya, yb, t), skipNode)) return false;
            }
            return true;
        }

        /// <summary>Is a plan point on pavement GatherPavement holds, within
        /// <see cref="PavedOnwardDyM"/> of <paramref name="y"/>?</summary>
        static bool PavementAt(CityMap map, Trims trims, Vector2 q, float y, int skipNode)
        {
            for (int k = 0; k < nearEdges.Count; k++)
            {
                var box = nearEdgeBox[k];
                if (q.x < box.x || q.x > box.z || q.y < box.y || q.y > box.w) continue;
                var ol = OutlineOf(map, trims, nearEdges[k]);
                if (ol.L == null || q.x < ol.minX || q.x > ol.maxX || q.y < ol.minZ || q.y > ol.maxZ) continue;
                for (int i = 1; i < ol.L.Length; i++)
                {
                    if (ol.BlockMisses(i, q.x, q.y, q.x, q.y)) { i += Outline.Block - 1; continue; }
                    if (TriInterval(ol.L[i - 1], ol.L[i], ol.R[i], q, Vector2.right, 0f, out _, out _) &&
                        Mathf.Abs(TriHeight(ol.L[i - 1], ol.L[i], ol.R[i], q) - y) <= PavedOnwardDyM) return true;
                    if (TriInterval(ol.L[i - 1], ol.R[i], ol.R[i - 1], q, Vector2.right, 0f, out _, out _) &&
                        Mathf.Abs(TriHeight(ol.L[i - 1], ol.R[i], ol.R[i - 1], q) - y) <= PavedOnwardDyM) return true;
                }
            }
            int skipFan = skipNode >= 0 ? FanKey(trims, skipNode) : -1;
            foreach (int n in nearFans)
            {
                if (n == skipFan) continue;
                var fan = fanPolys[n];
                if ((q - fan.centre).sqrMagnitude > fan.reach * fan.reach) continue;
                var T = fan.tris;
                for (int i = 0; i + 2 < T.Length; i += 3)
                    if (TriInterval(T[i], T[i + 1], T[i + 2], q, Vector2.right, 0f, out _, out _) &&
                        Mathf.Abs(TriHeight(T[i], T[i + 1], T[i + 2], q) - y) <= PavedOnwardDyM) return true;
            }
            return false;
        }

        /// <summary>
        /// A RAIL NEVER STANDS IN A LANE (2026-09-30). The rails a junction
        /// lays off two roads' geometry - a gore nose's block and its two tails
        /// (EmitNose), a fan's chord (BuildJunctions) - lay across a lane
        /// wherever those roads are drawn into each other: where North Caldwell
        /// Street merges into East 12th Street (node 11802) the nose between
        /// them ran from East 12th's edge, 0.65 m inside North Caldwell's one
        /// lane, to its far side, and a car at 70 km/h hit it; node 3578's
        /// chord rail stood over the same lane 0.3-0.8 m up (CityAudit's
        /// caldwell-11145). Such a line keeps only its pieces in no lane. A
        /// sample is in one where drawn pavement - a road's ribbon, or a fan
        /// other than <paramref name="skipNode"/>'s; one road, or two meeting
        /// on a seam under the rail - lies <see cref="RailLaneInsetM"/> to
        /// BOTH sides of the line (a rail on its own edge has pavement on one
        /// side only), at a height the rail stands in: its top over the
        /// lane's wheels, its foot less than
        /// RoadsideRules.OpenDropM over the lane (so the cut opens no drop the
        /// rail census fails). Fills <see cref="railRuns"/> with the parts
        /// (0..1 along a..b, world space) to lay; the whole line when clear.
        /// </summary>
        static void RailRunsOffLanes(CityMap map, Trims trims, Vector3 a, Vector3 b, int skipNode)
        {
            railRuns.Clear();
            var a2 = new Vector2(a.x, a.z); var b2 = new Vector2(b.x, b.z);
            float len = Vector2.Distance(a2, b2);
            if (len < 0.05f) { railRuns.Add((0f, 1f)); return; }
            var perp = new Vector2(a2.y - b2.y, b2.x - a2.x) / len;
            GatherNear(map, a2, b2, RailLaneInsetM + 0.5f);
            GatherPavement(map, trims);
            int n = Mathf.Max(2, Mathf.CeilToInt(len / RailLaneStepM) + 1);
            float h = 0.5f / (n - 1);
            int from = -1;
            for (int i = 0; i <= n; i++)
            {
                bool free = i < n && !InLaneAt(map, trims, Vector2.Lerp(a2, b2, (float)i / (n - 1)), perp, Mathf.Lerp(a.y, b.y, (float)i / (n - 1)), skipNode);
                if (free) { if (from < 0) from = i; continue; }
                if (from < 0) continue;
                float t0 = from == 0 ? 0f : (float)from / (n - 1) - h, t1 = i == n ? 1f : (float)(i - 1) / (n - 1) + h;
                if ((t1 - t0) * len >= RailLaneMinM) railRuns.Add((t0, t1));
                from = -1;
            }
        }

        const float RailLaneInsetM = 0.3f, RailLaneStepM = 0.25f, RailLaneMinM = 0.5f;
        static readonly List<(float t0, float t1)> railRuns = new List<(float, float)>(4);

        /// <summary>Lane both sides of the line at q: drawn pavement (one
        /// road's, or two roads' meeting on a seam under it) at a height the
        /// rail stands in.</summary>
        static bool InLaneAt(CityMap map, Trims trims, Vector2 q, Vector2 perp, float y, int skipNode) =>
            LaneUnder(map, trims, q + perp * RailLaneInsetM, y, skipNode) && LaneUnder(map, trims, q - perp * RailLaneInsetM, y, skipNode);

        static bool LaneUnder(CityMap map, Trims trims, Vector2 q, float y, int skipNode)
        {
            for (int k = 0; k < nearEdges.Count; k++)
            {
                var box = nearEdgeBox[k];
                if (q.x < box.x || q.x > box.z || q.y < box.y || q.y > box.w) continue;
                var ol = OutlineOf(map, trims, nearEdges[k]);
                if (ol.L == null || q.x < ol.minX || q.x > ol.maxX || q.y < ol.minZ || q.y > ol.maxZ) continue;
                if (OnOutline(ol, q, out float yl) && RailStandsIn(y, yl)) return true;
            }
            int skipFan = skipNode >= 0 ? FanKey(trims, skipNode) : -1;
            foreach (int nf in nearFans)
            {
                if (nf == skipFan) continue;
                var fan = fanPolys[nf];
                if ((q - fan.centre).sqrMagnitude > fan.reach * fan.reach) continue;
                if (OnFan(fan, q, out float yf) && RailStandsIn(y, yf)) return true;
            }
            return false;
        }

        static bool RailStandsIn(float yRail, float yLane) =>
            yLane - yRail < RailH - 0.15f && yRail - yLane < RoadsideRules.OpenDropM;

        static bool OnOutline(Outline ol, Vector2 q, out float y)
        {
            y = 0f;
            for (int i = 1; i < ol.L.Length; i++)
            {
                if (ol.BlockMisses(i, q.x, q.y, q.x, q.y)) { i += Outline.Block - 1; continue; }
                if (TriInterval(ol.L[i - 1], ol.L[i], ol.R[i], q, Vector2.right, 0f, out _, out _)) { y = TriHeight(ol.L[i - 1], ol.L[i], ol.R[i], q); return true; }
                if (TriInterval(ol.L[i - 1], ol.R[i], ol.R[i - 1], q, Vector2.right, 0f, out _, out _)) { y = TriHeight(ol.L[i - 1], ol.R[i], ol.R[i - 1], q); return true; }
            }
            return false;
        }

        static bool OnFan(FanPoly fan, Vector2 q, out float y)
        {
            y = 0f;
            var T = fan.tris;
            for (int i = 0; i + 2 < T.Length; i += 3)
                if (TriInterval(T[i], T[i + 1], T[i + 2], q, Vector2.right, 0f, out _, out _)) { y = TriHeight(T[i], T[i + 1], T[i + 2], q); return true; }
            return false;
        }

        /// <summary>A junction's rail from a to b (tile space) laid as
        /// <see cref="RailRunsOffLanes"/> leaves it, each piece capped and
        /// logged like <paramref name="rec"/>; the metres laid.</summary>
        static float EmitRailOffLanes(CityMap map, Trims trims, Vector3 origin, Vector3 a, Vector3 b, Vector2 inA, Vector2 inB, float drop,
                                      float v0, float v1, int skipNode, RailRecord rec, float hA = RailH, float hB = RailH)
        {
            RailRunsOffLanes(map, trims, a + origin, b + origin, skipNode);
            float laid = 0f;
            // a sloped rail (hA != hB, a nose's leg) rises from where its first
            // kept piece starts: a leg whose low end stood in a lane starts at
            // curb height where it leaves the lane, not square at mid height
            float tFirst = railRuns.Count > 0 && hA != hB ? railRuns[0].t0 : 0f;
            foreach (var (t0, t1) in railRuns)
            {
                var pa = Vector3.Lerp(a, b, t0); var pb = Vector3.Lerp(a, b, t1);
                var ia = Vector2.Lerp(inA, inB, t0).normalized; var ib = Vector2.Lerp(inA, inB, t1).normalized;
                float u0 = tFirst < 1f ? Mathf.Clamp01((t0 - tFirst) / (1f - tFirst)) : 1f, u1 = tFirst < 1f ? Mathf.Clamp01((t1 - tFirst) / (1f - tFirst)) : 1f;
                EmitRail(pa, pb, ia, ib, drop, true, true, Mathf.Lerp(v0, v1, t0), Mathf.Lerp(v0, v1, t1),
                         0f, 0f, RailOverhangM, RailOverhangM, Mathf.Lerp(hA, hB, u0), Mathf.Lerp(hA, hB, u1));
                laid += Vector3.Distance(pa, pb);
                if (railLog == null) continue;
                rec.a = pa + origin; rec.b = pb + origin; rec.inA = ia; rec.inB = ib; rec.overhang = RailOverhangM;
                railLog.Add(rec);
            }
            return laid;
        }

        /// <summary>Is the fan chord that meets this arm's corner railed?</summary>
        static bool FanCornerRailed(CityMap map, Trims trims, int node, CityMap.Edge e, int side)
        {
            var corners = railCornerScratch;
            FanCorners(map, trims, node, Vector3.zero, corners);
            if (corners.Count < 3) return false;
            for (int i = 0; i < corners.Count; i++)
            {
                // the arm's own corner, not a point the fan was carried out to along its edge line
                if (corners[i].edge != e.index || corners[i].side != side || corners[i].extra) continue;
                var prev = corners[(i + corners.Count - 1) % corners.Count];
                var next = corners[(i + 1) % corners.Count];
                return (!prev.mouthNext && ChordRailed(map, trims, node, prev, corners[i], Vector3.zero))
                    || (!corners[i].mouthNext && ChordRailed(map, trims, node, corners[i], next, Vector3.zero));
            }
            return false;
        }

        /// <summary>Does a rail run carry on across this edge end into the
        /// road beyond (so it neither caps nor flares there)? At a fan, into
        /// the rail on the chord that meets this side's corner.</summary>
        static bool RailContinuesPast(CityMap map, Trims trims, CityMap.Edge e, int node, int side)
        {
            if (trims.patch[node]) return FanCornerRailed(map, trims, node, e, side);
            var o = ThroughPartner(map, e, node);
            if (o == null) return false;
            return e.ElevatedAt(e.a == node ? 0f : e.length) || o.ElevatedAt(o.a == node ? 0f : o.length);
        }

        /// <summary>How far apart this road's drawn edge and the through
        /// road's may stand at a node for a barrier to carry on uncapped.</summary>
        const float EdgeContinueM = 0.75f;

        /// <summary>
        /// INVISIBLE COLLIDERS (2026-10-04, the owner's Uptown Loop at 1:18 on
        /// I-277: "I still hit an invisible wall right here"). A rail or
        /// median barrier carries on across an edge end uncapped only where
        /// the through road draws its edge on the same side within
        /// <see cref="EdgeContinueM"/> of where this one's ends. At I-277's
        /// left exit by East 4th Street (node 2264) the deck's left edge stood
        /// 4.9 m left of its line and the mainline beyond drew its own 1.5 m
        /// RIGHT of its line: the mainline's rail started there with no end
        /// face, inside the deck's left lane - nothing drawn faced the car,
        /// the audits' rays went into the open end, and PhysX (which collides
        /// a mesh from both sides) stopped the car. Not where the end is in a
        /// junction fan (FanCornerRailed decides those) or not at the node.
        /// </summary>
        static bool EdgeLinesUp(CityMap map, Trims trims, CityMap.Edge e, int node, int side, Section sec, Vector3 origin)
        {
            if (trims.patch[node]) return true;
            float sNode = e.a == node ? 0f : e.length;
            if (Mathf.Abs(sec.s - sNode) > 0.5f) return true;
            var o = ThroughPartner(map, e, node);
            if (o == null) return true;
            // the same side of the travel through the node: o leaves the node
            // where e arrives at it (or the other way round); head to head, the other side
            bool sameWay = (e.b == node) == (o.a == node);
            int oSide = sameWay ? side : -side;
            float so = o.a == node ? 0f : o.length;
            LaneExtents(map, trims, o, so, out float hl, out float hr);
            var t = o.TangentAt(so);
            var r = new Vector2(-t.y, t.x);
            var q = o.PointAt(so) + r * (oSide < 0 ? -hl : hr);
            var pe = sec.Edge(side);
            return Vector2.Distance(new Vector2(pe.x + origin.x, pe.z + origin.z), q) <= EdgeContinueM;
        }

        static void DecideSideFlags(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, Vector2 min, Vector2 max)
        {
            foreach (var _ in DecideSideFlagsSteps(map, trims, tm, e, min, max)) { }
        }

        /// <summary><see cref="DecideSideFlags"/> in steps (WP-09): eight spans
        /// of ground probes a step - a kilometre of I-277 was 72 ms in one.</summary>
        static IEnumerable<int> DecideSideFlagsSteps(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, Vector2 min, Vector2 max)
        {
            int n = sections.Count;
            spanFlags.Clear(); flareL.Clear(); flareR.Clear(); shiftL.Clear(); shiftR.Clear(); taperL.Clear(); taperR.Clear(); wbUL.Clear(); wbUR.Clear(); wbDL.Clear(); wbDR.Clear();
            for (int k = 0; k < n; k++) { spanFlags.Add(default); flareL.Add(0f); flareR.Add(0f); shiftL.Add(0f); shiftR.Add(0f); taperL.Add(1f); taperR.Add(1f); wbUL.Add(float.NaN); wbUR.Add(float.NaN); wbDL.Add(float.NaN); wbDR.Add(float.NaN); }
            dropCache.Clear();

            // the window of sections within FlagReachM of the tile
            int w0 = n, w1 = -1;
            var lo = min - Vector2.one * FlagReachM; var hi = max + Vector2.one * FlagReachM;
            for (int k = 0; k < n; k++)
            {
                var p = e.PointAt(sections[k].s);
                if (p.x < lo.x || p.x > hi.x || p.y < lo.y || p.y > hi.y) continue;
                w0 = Mathf.Min(w0, k); w1 = Mathf.Max(w1, k);
            }
            if (w1 < 0) yield break;
            w0 = Mathf.Max(0, w0 - 1); w1 = Mathf.Min(n - 1, w1 + 1);

            stepPart = 10;
            StructureEnds(map, trims, e, endScratch);
            bool barriered = Barriered(e);
            yield return 0;
            stepPart = 11;
            for (int i = 1; i < n; i++)
            {
                if ((i & 7) == 0) yield return 0;   // WP-09: eight spans a step
                var A = sections[i - 1]; var B = sections[i];
                var f = new SpanFlags
                {
                    // plan L3: a span is on structure by its MIDDLE, as the
                    // land under it is (ElevatedAt), so a deck's surface ends
                    // exactly at its structure end (StructureEnds): it used to
                    // run on over the span before it, to whatever section came
                    // first - a station, a vertex - and two twin decks ended
                    // that far out of step
                    elev = e.ElevatedAt(0.5f * (A.s + B.s)),
                    wedge = A.collapsed || B.collapsed,
                    // roads pass L5: the 2 cm span from a ramp's end section to
                    // its first collapsed one is nothing - its sides would lie
                    // ACROSS the lane at the nose (a deck rail stood there)
                    skip = (A.collapsed && B.collapsed) || (A.zoneEnd && B.collapsed) || (B.zoneEnd && A.collapsed),
                    decided = i > w0 && i <= w1,
                };
                bool approach = false;
                foreach (var se in endScratch)
                    if (B.s > se - ApproachRailM + 0.01f && A.s < se + ApproachRailM - 0.01f) { approach = true; break; }
                f.approach = approach;
                for (int side = -1; side <= 1; side += 2)
                {
                    var sf = new SideFlags
                    {
                        gap = SpanInGoreGap(e.index, side, A.s, B.s)
                              || (A.innerSide == side && (A.clippedIn || A.collapsed))
                              || (B.innerSide == side && (B.clippedIn || B.collapsed)),
                    };
                    // PLAN A2: one structure with the road beside it - a twin
                    // deck's inner side, or the approach its median carries
                    // on over. Nothing stands on it (no parapet, approach
                    // rail, Jersey or cut wall); the run's owner draws the
                    // median (EmitSide).
                    if (!sf.gap && !f.skip && UnionSideHere(map, trims, e, side, A, B, tm.origin) != null)
                    {
                        sf.union = true;
                        f[side] = sf;
                        continue;
                    }
                    // Two roads squeezed together share ONE barrier or rail
                    // between them: the lower-numbered edge draws it, and the
                    // other stands down only where it can SEE that it does.
                    int nb = A.Nb(side) >= 0 ? A.Nb(side) : B.Nb(side);
                    var nbSec = A.Nb(side) >= 0 ? A : B;
                    bool share = nb >= 0 && nb < e.index;
                    if (!sf.gap && !f.skip && f.decided)
                    {
                        // A span squeezed at one end only: the step to that
                        // neighbour at the OTHER end too. Sections can be ten
                        // metres apart, and near a node, where one road's
                        // height turns away from the other's, the step grew
                        // past a ledge between them (North College Street's
                        // two carriageways, 0.26 m apart at the squeezed end
                        // and 0.33 m at the unsqueezed one; WP-04).
                        int squeeze = 0;
                        if (nb >= 0 && !f.wedge && (A.Nb(side) < 0) != (B.Nb(side) < 0))
                        {
                            float st = StepToNeighbour(map, tm, A.Nb(side) < 0 ? A : B, side, nb);
                            if (!float.IsNaN(st)) squeeze = SqueezeDrop(map, trims, e, nbSec, side, st);
                        }
                        bool wanted = f.elev || approach
                                      || (!f.wedge && ((DropAt(map, trims, tm, e, i - 1, side) | DropAt(map, trims, tm, e, i, side)
                                                        | DropMid(map, trims, tm, e, i, side) | squeeze) & DropWarrant) != 0);
                        // A neighbour more than a ledge ABOVE us draws its rail
                        // on its own edge, over our heads: it guards nothing
                        // on ours (WP-04).
                        if (wanted && share && NeighbourDrawsRail(map, trims, e, nbSec, side) && !NeighbourAbove(map, nbSec, side)) wanted = false;
                        // Two decks carrying traffic the SAME way, squeezed
                        // together (a ramp on structure beside its mainline,
                        // a collector beside its carriageway), are one deck:
                        // the strip between them is floored flush (EmitSide's
                        // squeeze strip) and no parapet stands in it - it
                        // stood between lanes of one direction (owner
                        // 2026-10-04, "stray medians on 277"). Opposing
                        // traffic keeps its one shared barrier.
                        if (wanted && f.elev && nb >= 0 && SameWayDeckBeside(map, e, nbSec, side)) { wanted = false; sameWayDeckSides.Add(e.index); }
                        sf.rail = wanted;
                        // on a retaining face only where the verge cannot grade
                        // the land (DropFrom); an approach rail over land a verge
                        // reaches stands over that verge
                        if (sf.rail && !f.elev)
                            sf.retain = ((DropAt(map, trims, tm, e, i - 1, side) | DropAt(map, trims, tm, e, i, side) | squeeze) & DropUngraded) != 0;
                    }
                    // Decided from data alone (no ground probe), for every span
                    // of the edge: a retaining run's length and gaps reach
                    // tens of metres, and every tile must see the same run.
                    if (barriered && !f.elev && !f.wedge && !sf.gap && !approach)
                    {
                        bool nbDraws = share && NeighbourDrawsBarrier(map, trims, e, nbSec, side);
                        // THE MEDIAN SIDE ONLY. A Jersey wall stood on both
                        // edges of every freeway carriageway, and the outside
                        // shoulder of a real interstate is open verge — the
                        // owner named it at once: "outside shoulder walls they
                        // don't have in real life". R is the LEFT of travel
                        // (see BuildSections), which on a one-way carriageway
                        // is the median. The outside gets a wall only where
                        // the road runs in a CUT (below).
                        if (side > 0) sf.median = !nbDraws && !sf.rail;
                        else
                        {
                            byte why = 0;
                            sf.cut = !nbDraws && InCut(map, e, tm, A, B, -A.right, out why);
                            if (sf.cut) sf.cutWhy = why;
                        }
                    }
                    f[side] = sf;
                }
                spanFlags[i] = f;
            }

            yield return 0;
            stepPart = 12;
            // THE RETAINING WALLS ARE RUNS. Per span, one DEM sample against a
            // 2 m threshold flickered the wall on and off; a gap of up to
            // CutRunEndSpans spans is closed, and a run shorter than
            // CutRunMinSpans is not built at all.
            for (int i = 1; i < n; i++)
            {
                if (!spanFlags[i].l.cut) continue;
                int j = i + 1;
                while (j < n && j - i <= CutRunEndSpans && !spanFlags[j].l.cut) j++;
                if (j < n && j - i > 1 && j - i <= CutRunEndSpans)
                    for (int k = i + 1; k < j; k++)
                    {
                        var fk = spanFlags[k];
                        if (fk.elev || fk.wedge || fk.l.gap || fk.approach) break;
                        fk.l.cut = true; spanFlags[k] = fk;
                    }
            }
            for (int i = 1; i < n;)
            {
                if (!spanFlags[i].l.cut) { i++; continue; }
                int j = i;
                while (j < n && spanFlags[j].l.cut) j++;
                if (j - i < CutRunMinSpans)
                    for (int k = i; k < j; k++) { var fk = spanFlags[k]; fk.l.cut = false; spanFlags[k] = fk; }
                i = j;
            }

            // NO ISOLATED SHORT PIECES (hotfix 2026-10-03, the owner's "stray
            // concrete median blocks between roads where they shouldn't
            // exist"): a median Jersey or a cut wall shorter than
            // MinMedianRunM that ends inside the edge at both ends, with no
            // barrier of any kind carrying on beside either end (a rail, a
            // wall, a union's median: that is one barrier changing kind, not
            // a block), is not drawn. A run reaching a node carries on into
            // the next road.
            // Decided from the spans' data alone, as the runs are, so every
            // tile drops the same piece.
            if (!KeepShortBarriers)
                for (int side = -1; side <= 1; side += 2)
                    for (int i = 1; i < n;)
                    {
                        var si = spanFlags[i][side];
                        bool med = si.median, cut = si.cut && !si.rail;
                        if (!med && !cut) { i++; continue; }
                        int j = i;
                        while (j < n && (med ? spanFlags[j][side].median : spanFlags[j][side].cut && !spanFlags[j][side].rail)) j++;
                        bool Bare(int k) { var b = spanFlags[k][side]; return !(b.rail || b.cut || b.median || b.union || b.retain); }
                        if (i > 1 && j < n && Bare(i - 1) && Bare(j) && sections[j - 1].s - sections[i - 1].s < MinMedianRunM)
                            for (int k = i; k < j; k++)
                            {
                                var fk = spanFlags[k]; var sk = fk[side];
                                if (med) sk.median = false; else sk.cut = false;
                                fk[side] = sk; spanFlags[k] = fk;
                            }
                        i = j;
                    }

            // THE RAIL END (leftover item 2, 2026-10-03; the owner's W 5th
            // frame: a parapet starting on its approach as a blunt concrete
            // block, its flat grey end square to the traffic). DOT practice,
            // for every rail run that ends ON THE GROUND into nothing (not into
            // a gore, a union, a skipped span or another barrier: those keep
            // their ends - the nose's V, the union's median, the hand-over):
            //   W-BEAM  the open roadside past the end holds WBeamLenM: the
            //           concrete carries on TaperLenM, sloped down to a curb,
            //           and a W-beam on posts runs from the full-height
            //           parapet along that slope (the transition) and on, its
            //           terminal flared away from the road and turned down
            //           into the ground (EmitWBeam, CityPoles draws it).
            //   SLOPED  it holds half of TaperLenM: the concrete carries on
            //           (up to TaperLenM, never past its share of the open
            //           roadside) and is sloped down to a curb over that
            //           length (1:10 over the full one), as a median Jersey's.
            //   INSIDE  it does not: the run's own last TaperLenM slopes down.
            //   CONNECT another barrier stands on within two lead-ins' length
            //           (two bridges close together): the gap is closed with
            //           the rail at full height (the Roadside Design Guide
            //           closes short gaps between barrier runs) - no ends.
            // The open roadside: decided spans of this edge with no barrier,
            // gap, union, deck, wedge, approach, squeeze or retaining face,
            // walked at most RailEndWalkM. Past two lead-ins' length another
            // barrier's stretch is shared: each end gets half, so two facing
            // lead-ins never meet. Planned from the flags alone before any is
            // applied, and all of it within FlagReachM of the tile, so every
            // tile plans every end the same way.
            for (int side = -1; side <= 1; side += 2)
            {
                int KindAt(int i)
                {
                    if (i < 1 || i >= n) return KindNone;
                    var s = spanFlags[i][side];
                    return s.rail ? KindRail : s.cut ? KindCut : s.median ? KindMedian : KindNone;
                }
                bool Open(int sp)
                {
                    if (sp < 1 || sp >= n) return false;
                    var f = spanFlags[sp]; var sf = f[side];
                    return f.decided && !f.elev && !f.wedge && !f.skip && !f.approach
                           && !sf.gap && !sf.union && !sf.rail && !sf.cut && !sf.median && !sf.retain
                           && sections[sp - 1].Strip(side) < 0f && sections[sp].Strip(side) < 0f
                           && !sections[sp - 1].collapsed && !sections[sp].collapsed;
                }
                endPlan.Clear();
                for (int k = w0; k <= w1; k++)
                {
                    int kL = KindAt(k), kR = KindAt(k + 1);
                    if ((k >= 1 && !spanFlags[k].decided) || (k + 1 < n && !spanFlags[k + 1].decided)) continue;
                    bool lineL = kL == KindRail && kR == KindNone, lineR = kR == KindRail && kL == KindNone;
                    if (!lineL && !lineR) continue;
                    // a rail carrying on into the next road at the node is no end
                    if (k == 0 && lineR && RailContinuesPast(map, trims, e, e.a, side)) continue;
                    if (k == n - 1 && lineL && RailContinuesPast(map, trims, e, e.b, side)) continue;
                    int dir = lineL ? 1 : -1;
                    int railSpan = lineL ? k : k + 1, openSpan = lineL ? k + 1 : k;
                    if (sections[k].elev || spanFlags[railSpan].elev) continue;
                    // ...nor a rail standing at a deck's edge (the deck beside it has its own reasons)
                    // ...nor one handing over to a union's median (its owner
                    // draws that, and turns its own ends)
                    if (openSpan >= 1 && openSpan < n && (spanFlags[openSpan].skip || spanFlags[openSpan].elev || spanFlags[openSpan].wedge
                                                          || spanFlags[openSpan][side].union)) continue;
                    // a rail running into a gore on the ground slopes down
                    // inside its own run (nothing is laid on the gore)
                    if (openSpan >= 1 && openSpan < n && spanFlags[openSpan][side].gap)
                    {
                        endPlan.Add((k, dir, EndInside, 0f, k));
                        continue;
                    }
                    float room; bool shared = false;
                    // how far the W-beam's corridor (its flare, posts and box)
                    // keeps off every other road's pavement
                    float wide = float.MaxValue;
                    int j = k;
                    while (true)
                    {
                        int sp = dir > 0 ? j + 1 : j;
                        float d = Mathf.Abs(sections[j].s - sections[k].s);
                        if (sp < 1 || sp >= n) { room = d - RailEndClearM; break; }
                        if (!Open(sp))
                        {
                            shared = KindAt(sp) != KindNone;
                            room = shared ? d : d - RailEndClearM;
                            break;
                        }
                        if (wide == float.MaxValue && d < WBeamLenM + RailEndClearM && !WBeamCorridorClear(map, e, tm.origin, sections[sp - 1], sections[sp], side))
                            wide = d;
                        j += dir;
                        if (Mathf.Abs(sections[j].s - sections[k].s) >= RailEndWalkM) { room = RailEndWalkM; break; }
                    }
                    int how;
                    if (shared && room < 2f * WBeamLenM + RailEndClearM) how = EndConnect;
                    else
                    {
                        if (shared) room = 0.5f * room - 0.5f * RailEndClearM;
                        how = room >= WBeamLenM && wide >= WBeamLenM ? EndWBeam : room >= 0.5f * TaperLenM ? EndSloped : EndInside;
                    }
                    endPlan.Add((k, dir, how, room, j));
                }
                var taper = side < 0 ? taperL : taperR;
                void MarkRail(int sp)
                {
                    var fs = spanFlags[sp]; var ss = fs[side]; ss.rail = true; ss.retain = false; fs[side] = ss; spanFlags[sp] = fs;
                }
                foreach (var (k, dir, how0, room, jEnd) in endPlan)
                {
                    int how = how0;
                    var pk = e.PointAt(sections[k].s);
                    bool mineEnd = pk.x >= min.x && pk.x < max.x && pk.y >= min.y && pk.y < max.y;   // counted by the tile it is in
                    if (how == EndConnect)
                    {
                        // the open stretch to the next barrier, rail at full height
                        for (int q = k; q != jEnd; q += dir) MarkRail(dir > 0 ? q + 1 : q);
                        if (mineEnd) tm.railEndsConnected++;
                        continue;
                    }
                    // how far the concrete may carry on: to the first section
                    // TaperLenM past the end, never past this end's share
                    int jx = k; float ext = 0f;
                    for (int q = k; ; q += dir)
                        {
                            int sp = dir > 0 ? q + 1 : q, qn = q + dir;
                            if (sp < 1 || sp >= n) break;
                            float dn = Mathf.Abs(sections[qn].s - sections[k].s);
                            if (dn > room + 1e-3f) break;
                            jx = qn; ext = dn;
                            if (dn >= TaperLenM) break;
                        }
                    if (how != EndInside && ext < 0.5f * TaperLenM) how = EndInside;
                    if (how == EndInside)
                    {
                        // A rail standing over a drop to its very end - on a
                        // retaining face, or on a deck - keeps its full height
                        // (the rail census: a drop is guarded to the end).
                        bool overDrop = false;
                        for (int j = k; j >= 0 && j < n && !overDrop; j -= dir)
                        {
                            if (Mathf.Abs(sections[j].s - sections[k].s) >= TaperLenM) break;
                            int spb = dir > 0 ? j : j + 1;
                            if (spb < 1 || spb >= n || KindAt(spb) != KindRail) break;
                            overDrop = spanFlags[spb].elev || spanFlags[spb][side].retain;
                        }
                        if (overDrop && ext < ShortSlopeM) { if (mineEnd) tm.railEndsKept++; continue; }
                        if (!overDrop)
                        {
                            // the run's own last TaperLenM, back from the end
                            for (int j = k; j >= 0 && j < n; j -= dir)
                            {
                                float d = Mathf.Abs(sections[j].s - sections[k].s);
                                if (d >= TaperLenM) break;
                                taper[j] = Mathf.Min(taper[j], d / TaperLenM);
                                if (KindAt(dir > 0 ? j : j + 1) != KindRail) break;
                            }
                            if (mineEnd) tm.railEndsInside++;
                            continue;
                        }
                        // over a drop to its very end, with a little open
                        // roadside past it: sloped there, short and steep,
                        // rather than square
                        how = EndSloped;
                        if (mineEnd) tm.railEndsShort++;
                    }
                    // the concrete carries on past its end, sloped from full
                    // height down to a curb over that length
                    for (int q = k; q != jx; q += dir) MarkRail(dir > 0 ? q + 1 : q);
                    for (int q = k; ; q += dir)
                    {
                        float d = Mathf.Abs(sections[q].s - sections[k].s);
                        taper[q] = Mathf.Min(taper[q], Mathf.Clamp01(1f - d / ext));
                        if (q == jx) break;
                    }
                    if (how == EndSloped) { if (mineEnd) tm.railEndsSloped++; continue; }
                    // ...and the W-beam from the parapet's full height on
                    var wb = WbList(side, dir);
                    for (int q = k; q >= 0 && q < n; q += dir)
                    {
                        float u = Mathf.Abs(sections[q].s - sections[k].s);
                        wb[q] = u;
                        if (u >= WBeamLenM) break;
                    }
                    if (mineEnd) tm.railEndsWBeam++;
                }
            }

            // Run ends, caps and flares, by KIND of barrier: a rail, a
            // retaining wall, a median Jersey barrier. Where one kind hands
            // over to another (a median barrier to the approach rail twenty
            // metres before a bridge, a cut wall to the same) BOTH ends are
            // capped and neither flares: a flared rail beside a barrier that
            // carries straight on left the barrier's open end facing traffic.
            for (int side = -1; side <= 1; side += 2)
            {
                var flare = side < 0 ? flareL : flareR;
                int Kind(int i)
                {
                    if (i < 1 || i >= n) return KindNone;
                    var s = spanFlags[i][side];
                    return s.rail ? KindRail : s.cut ? KindCut : s.median ? KindMedian : KindNone;
                }
                bool Decided(int i) => i >= 1 && i < n && spanFlags[i].decided;
                // A section a flare may not move off the edge: on a deck, at
                // the grounded end of a span drawn as deck, or beside a rail
                // standing on a retaining face. There is nothing past those
                // edges to stand a flared rail over: the flare lifted the rail
                // off the fascia or the face and left a slot a hand to 0.8 m
                // wide straight down beside the tarmac (a 38 cm to 1.3 m lip at
                // every such run end in the roadside audit, and the "open
                // grounded ledge" and approach runs it could not see a rail
                // across).
                bool Fixed(int j)
                {
                    if (sections[j].elev) return true;
                    for (int sp = j; sp <= j + 1; sp++)
                        if (sp >= 1 && sp < n && (spanFlags[sp].elev || spanFlags[sp][side].retain)) return true;
                    return false;
                }
                for (int k = w0; k <= w1; k++)
                {
                    int kindL = Kind(k), kindR = Kind(k + 1);
                    // an edge end: a rail may carry on into the next road, and
                    // a median barrier always does (it never capped at a node)
                    // ...but only where the road beyond draws its edge on this
                    // side where this one ends (EdgeLinesUp): a run whose
                    // "continuation" starts metres across the lanes was left
                    // OPEN - no end face, so nothing drawn faces the car while
                    // PhysX stops it dead (the owner's I-277 invisible wall)
                    if (k == 0 && (kindR == KindMedian || kindR == KindRail && RailContinuesPast(map, trims, e, e.a, side))
                        && EdgeLinesUp(map, trims, e, e.a, side, sections[0], tm.origin)) kindL = kindR;
                    if (k == n - 1 && (kindL == KindMedian || kindL == KindRail && RailContinuesPast(map, trims, e, e.b, side))
                        && EdgeLinesUp(map, trims, e, e.b, side, sections[n - 1], tm.origin)) kindR = kindL;
                    // the window's own ends are not run ends: nothing beyond is decided
                    if ((k >= 1 && !Decided(k)) || (k + 1 < n && !Decided(k + 1))) continue;
                    if (kindL == kindR) continue;
                    if (kindL != KindNone) { var rf = spanFlags[k]; var rs = rf[side]; rs.capEnd = true; rf[side] = rs; spanFlags[k] = rf; }
                    if (kindR != KindNone) { var rf = spanFlags[k + 1]; var rs = rf[side]; rs.capStart = true; rf[side] = rs; spanFlags[k + 1] = rf; }
                    // Nothing moves outward where the side is squeezed against
                    // a parallel road: its lanes are 0.3 m beyond the edge, and
                    // a flare's tip stood half a metre into them.
                    if (kindL != KindNone && kindR != KindNone)
                    {
                        // A barrier's traffic face is AT the edge and a rail's
                        // RailW inside it, so the rail's capped end stood 0.3 m
                        // proud of the barrier it took over from: a blunt face
                        // for a car scraping along the barrier. The rail eases
                        // out to meet it flush, at the flare's own ratio.
                        if ((kindL == KindRail) == (kindR == KindRail) || sections[k].elev || sections[k].Strip(side) >= 0f) continue;
                        var shift = side < 0 ? shiftL : shiftR;
                        int dirT = kindL == KindRail ? -1 : 1;
                        float easeM = RailW / RoadsideRules.EndFlareRatio;
                        for (int j = k; j >= 0 && j < n; j += dirT)
                        {
                            float d = Mathf.Abs(sections[j].s - sections[k].s);
                            if (d >= easeM || sections[j].elev || sections[j].Strip(side) >= 0f) break;
                            shift[j] = Mathf.Max(shift[j], RailW - RoadsideRules.EndFlareRatio * d);
                            if (Kind(dirT > 0 ? j + 1 : j) != KindRail) break;
                        }
                        continue;
                    }
                    // Only a rail or wall running out into nothing flares; not
                    // a median barrier (its neighbour may share the strip), not
                    // into a gap (it would stand in the surface beyond), not on
                    // a deck, and not at a node the road carries on through
                    // (every node of a long run would zig-zag).
                    int ending = kindL != KindNone ? kindL : kindR;
                    bool lineL = kindL != KindNone;
                    int otherSpan = lineL ? k + 1 : k;
                    // THE SLOPED END (hotfix 2026-10-03): a median Jersey's end
                    // faces the traffic beside it: turned down over TaperLenM
                    // instead of capped square. (A rail stays full height: it
                    // guards a drop. Where one runs out into a gore on a deck,
                    // the nose's V stands in front of its end - EmitNose.)
                    if (ending == KindMedian)
                    {
                        var taper = side < 0 ? taperL : taperR;
                        int dirT = lineL ? -1 : 1;
                        for (int j = k; j >= 0 && j < n; j += dirT)
                        {
                            float d = Mathf.Abs(sections[j].s - sections[k].s);
                            if (d >= TaperLenM) break;
                            taper[j] = Mathf.Min(taper[j], d / TaperLenM);
                            if (Kind(dirT > 0 ? j + 1 : j) != ending) break;
                        }
                    }
                    if (ending == KindMedian) continue;
                    // a W-beam lead-in carries the line on from a sloped
                    // parapet end (leftover item 2): it flares, not the concrete
                    if (ending == KindRail && OnWBeam(side, k)) continue;
                    bool otherGap = otherSpan >= 1 && otherSpan < n && (spanFlags[otherSpan][side].gap || spanFlags[otherSpan][side].union || spanFlags[otherSpan].skip);
                    bool atNode = (k == 0 && !lineL) || (k == n - 1 && lineL);
                    if (otherGap || Fixed(k) || sections[k].Strip(side) >= 0f || (atNode && trims.mitre[k == 0 ? e.a : e.b])) continue;
                    // the flare, along the run away from this end, over the
                    // grounded sections within FlareLenM
                    int dir = lineL ? -1 : 1;
                    for (int j = k; j >= 0 && j < n; j += dir)
                    {
                        float d = Mathf.Abs(sections[j].s - sections[k].s);
                        if (d >= FlareLenM || Fixed(j) || sections[j].Strip(side) >= 0f) break;
                        flare[j] = Mathf.Max(flare[j], RoadsideRules.EndFlareRatio * (FlareLenM - d));
                        int spanNext = dir > 0 ? j + 1 : j;
                        if (Kind(spanNext) != ending) break;
                    }
                }
            }
        }

        const int KindNone = 0, KindRail = 1, KindCut = 2, KindMedian = 3;

        /// <summary>What the land beside a drawn edge point warrants
        /// (<see cref="DropFrom"/>): a barrier, and whether the verge cannot
        /// grade it — the rail then stands on a retaining face.</summary>
        const int DropWarrant = 1, DropUngraded = 2;
        static readonly Dictionary<int, int> dropCache = new Dictionary<int, int>();
        static readonly Vector3[] dropProf = new Vector3[4];

        /// <summary>How much lower a squeezed neighbour may stand before the
        /// barrier it draws on the strip between us stops guarding OUR edge:
        /// past this its Jersey top is under the lower of the roadside
        /// audit's barrier heights over our tarmac. I-277's carriageways run
        /// a metre apart in height through uptown with a 0.3 m strip, and the
        /// lower one's barrier stood 19 cm below the upper one's surface.</summary>
        const float SharedGuardDyM = BarrierH - 0.35f;

        /// <summary>
        /// What the land beside this section's edge warrants, as
        /// <see cref="DropWarrant"/> | <see cref="DropUngraded"/> bits (see
        /// <see cref="DropFrom"/>). A side squeezed against a parallel road
        /// is warranted — on a retaining face — only where that road stands
        /// more than <see cref="SharedGuardDyM"/> below it, or more than
        /// RoadsideRules.LedgeStepM below it with no median barrier on the
        /// strip between.
        /// </summary>
        static int DropAt(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, int k, int side)
        {
            int key = k * 2 + (side > 0 ? 1 : 0);
            if (dropCache.TryGetValue(key, out int drop)) return drop;
            var sec = sections[k];
            drop = 0;
            if (!sec.collapsed)
            {
                var ed = sec.Edge(side);
                if (sec.Strip(side) < 0f)
                    drop = DropFrom(map, trims, new Vector2(ed.x + tm.origin.x, ed.z + tm.origin.z), sec.Out(side), ed.y, ShoulderOf(e, side), !MedianBarriered(e, side));
                else
                {
                    // A squeeze strip is flat, so the step down to a lower
                    // neighbour stands in the middle of it. A strip is never
                    // wider than 1.2 m, which grades 30 cm at 1V:4H: a taller
                    // step is a ledge a wheel drops off with nothing between
                    // the lanes and it (North Caldwell Street's e2365, 42 cm
                    // over e5506 across 30 cm), and it is warranted — on a
                    // retaining face — unless a median barrier on the strip
                    // stands between. Past SharedGuardDyM no barrier the lower
                    // road draws guards this edge.
                    float step = ed.y - map.edges[sec.Nb(side)].YAt(sec.NbAt(side));
                    drop = SqueezeDrop(map, trims, e, sec, side, step);
                }
            }
            dropCache[key] = drop;
            return drop;
        }

        /// <summary>
        /// What a squeeze strip warrants, from the step between this edge and
        /// the neighbour across it (positive: the neighbour is LOWER). A
        /// lower neighbour: see <see cref="DropAt"/>. A HIGHER one (WP-04):
        /// more than RoadsideRules.LedgeStepM up, its edge stands on a
        /// retaining face at the far side of the strip, and a wheel of ours
        /// drops into the slot at that face's foot, down to whatever the
        /// lattice is there (a 0.4 m slot beside the Tyvola ramp e328, e1896
        /// 1.3 m over it; a 3.7 m slot between I-77's viaduct carriageways a
        /// metre apart). The Roadside Design Guide shields a face in the
        /// clear zone: a rail on OUR edge, over the strip, unless a median
        /// barrier already stands there. On the old filtered ground adjacent
        /// roads were never that far apart.
        /// </summary>
        static int SqueezeDrop(CityMap map, Trims trims, CityMap.Edge e, Section nbSec, int side, float step)
        {
            if (step > SharedGuardDyM || (step > RoadsideRules.LedgeStepM && !StripBarriered(map, trims, e, nbSec, side)))
                return DropWarrant | DropUngraded;
            if (-step > RoadsideRules.LedgeStepM && !StripBarriered(map, trims, e, nbSec, side))
                return DropWarrant;
            return 0;
        }

        /// <summary>The step (positive: the neighbour lower) from a section's
        /// drawn edge to squeezed neighbour <paramref name="nb"/>, measured at
        /// the edge point's foot on the neighbour: for the end of a span that
        /// is not itself squeezed, against the neighbour the other end names.
        /// NaN where the foot is not beside the neighbour's pavement.</summary>
        static float StepToNeighbour(CityMap map, TileMeshes tm, Section sec, int side, int nb)
        {
            if (sec.collapsed) return float.NaN;
            var ed = sec.Edge(side);
            var o = map.edges[nb];
            var pW = new Vector2(ed.x + tm.origin.x, ed.z + tm.origin.z);
            CityElevation.ProjectOn(o, pW, out float at);
            if (at <= 0.01f || at >= o.length - 0.01f) return float.NaN;
            if (Vector2.Distance(pW, o.PointAt(at)) > o.HalfMax + 1.5f) return float.NaN;
            return ed.y - o.YAt(at);
        }

        /// <summary>Does the squeezed neighbour stand more than a ledge above
        /// this edge? Then the rail it draws on its own edge is over our
        /// head, not between our lanes and the slot at its face.</summary>
        static bool NeighbourAbove(CityMap map, Section sec, int side) =>
            sec.Nb(side) >= 0 && map.edges[sec.Nb(side)].YAt(sec.NbAt(side)) - sec.Edge(side).y > RoadsideRules.LedgeStepM;

        /// <summary>The same question halfway along a span. Sections can be
        /// ten metres apart and the lattice is eight: a pit narrower than a
        /// span, missed at both ends, left a rail gap the roadside audit's
        /// one-metre census would fail.</summary>
        static int DropMid(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, int i, int side)
        {
            var A = sections[i - 1]; var B = sections[i];
            if (A.collapsed || B.collapsed || A.Strip(side) >= 0f || B.Strip(side) >= 0f) return 0;
            var ed = (A.Edge(side) + B.Edge(side)) * 0.5f;
            var outw = A.Out(side) + B.Out(side);
            if (outw.sqrMagnitude < 1e-6f) return 0;
            return DropFrom(map, trims, new Vector2(ed.x + tm.origin.x, ed.z + tm.origin.z), outw.normalized, ed.y, ShoulderOf(e, side), !MedianBarriered(e, side));
        }

        /// <summary>
        /// What the land beyond a drawn edge point (world plan position,
        /// surface height, outward unit vector) warrants. The owner's rule is
        /// that a road meets the ground by DOT grading, and a barrier stands
        /// only where grading cannot make the roadside recoverable. So the
        /// verge the tile would lay here is SOLVED:
        ///
        ///   * where it cannot reach the ground — it runs out of VergeMaxRunM
        ///     still above the lattice, or comes to a road more than
        ///     RoadsideRules.OpenDropM below that it cannot connect to — the
        ///     drop is warranted AND ungraded: a rail on a retaining face (a
        ///     smaller step down onto a road beside is a kerb between two
        ///     carriageways, and falls to the test below);
        ///   * where it can, the only warrant left is a CRITICAL fall within
        ///     RoadsideRules.WarrantReachM on the FINISHED surface (the verge
        ///     where it reaches that far, else the lattice or the road beyond
        ///     it): the street 30 m from the 5.75 m trench that is graded flat
        ///     past its verge and then meets the lattice leaning into the
        ///     trench's protected band. That rail stands over the graded verge.
        ///
        /// It used to rail every edge whose LATTICE 1.5 m out was a metre
        /// down, and to stand that rail on a retaining face wherever the
        /// lattice 0.3 m out was 40 cm down — a wall on every approach whose
        /// ground sat 38 cm under a cell still leaning to a dug soffit, which
        /// a verge grades in two and a half metres.
        /// </summary>
        static int DropFrom(CityMap map, Trims trims, Vector2 pW, Vector2 outw, float y, float shoulder, bool ledges = true)
        {
            GatherNear(map, pW, pW, VergeMaxRunM + RoadsideRules.ToeTuckRunM);
            var sh = new StripShape { shoulder = shoulder, maxRun = VergeMaxRunM };
            bool laid = SolveStrip(map, trims, pW, y, outw, sh, dropProf, out bool graded, out float stoppedBy);
            if (Ungraded(laid, graded, stoppedBy, pW, y, dropProf, ledges)) return DropWarrant | DropUngraded;
            float reach = RoadsideRules.WarrantReachM;
            float yFinished = SurfaceOut(map, trims, pW, outw, y, reach);
            if (laid)
            {
                float e1 = Vector2.Distance(new Vector2(dropProf[1].x, dropProf[1].z), pW);
                float e2 = Vector2.Distance(new Vector2(dropProf[2].x, dropProf[2].z), pW);
                if (e2 >= reach && e2 > e1 + 1e-3f)
                    yFinished = Mathf.Max(yFinished, Mathf.Lerp(dropProf[1].y, dropProf[2].y, (reach - e1) / (e2 - e1)));
            }
            return RoadsideRules.IsCriticalFall(y - yFinished, reach) ? DropWarrant : 0;
        }

        /// <summary>
        /// Is a verge cross-section's land one grading cannot make recoverable
        /// (<see cref="SolveStrip"/>'s outputs for an edge point at
        /// <paramref name="y"/>)? Then the edge is warranted a barrier on a
        /// retaining face (<see cref="DropFrom"/>), and nothing meets that
        /// side as a verge (<see cref="CornerFill"/>).
        ///
        /// A verge stopped by a road beside that it cannot connect to is a
        /// step down onto that road — a kerb between two carriageways, not a
        /// roadside a car falls off — unless the step is itself the drop (a
        /// rail in the other road's lanes was the price of calling every such
        /// step ungraded: two South McDowell carriageways 6 cm apart).
        ///
        /// And a kerb is an inch or two, not a ledge. Where not even the steep
        /// connector reaches the road beside (the verge tucks straight down
        /// short of it), a step down deeper than RoadsideRules.LedgeStepM from
        /// the verge's last point is a ledge a wheel drops off beside the
        /// lanes (e4242 42 cm over e1324's verge 45 cm out, e5252 43 cm over
        /// e9986's): the land cannot be graded in the room there is, and the
        /// DOT answer to two carriageways a level apart with no room between
        /// for a 1V:4H slope is a barrier. Only where the verge got out from
        /// the edge at all: a road drawn INTO this one (its pavement at the
        /// edge already) is the squeeze's and the clip's to split, and a rail
        /// there stood in its lanes (e14103 over North Davidson Street, North
        /// Kings Drive over Armory Drive). And not behind a freeway's median
        /// barrier (<paramref name="ledges"/> false): the Jersey wall on the
        /// edge already stands between the lanes and the step, and it stood
        /// as a rail on a retaining face along 430 m of the audited medians.
        /// Nor on a junction fan's chord, where the road a level below is
        /// another junction's mouth (<see cref="ChordRailed"/>).
        /// </summary>
        static bool Ungraded(bool laid, bool graded, float stoppedBy, Vector2 pW, float y, Vector3[] prof, bool ledges = true)
        {
            if (graded) return false;
            if (float.IsNaN(stoppedBy)) return true;
            // A road more than OpenDropM below is a drop - unless the verge
            // CONNECTED down to it no steeper than the traversable 1V:3H, which
            // is graded ground and no critical fall. A hard 1.0 m line here
            // flickered: the ramp e5219 beside I-77 (e2739) 3.7 m off, 0.98-
            // 1.00 m above it, stood a rail on a retaining face where WP-13's
            // road grid put it 1.00 m up and a graded verge where 0.98 m, and
            // the verge's end beside the wall was a 0.49 m ledge a wheel off
            // I-77 dropped from.
            if (y - stoppedBy > RoadsideRules.OpenDropM && !TraversableConnector(laid, stoppedBy, pW, y, prof)) return true;
            // measured to the road beside's VERGE, where a wheel lands: an
            // inch under its pavement (a 0.29 m step to e9986's pavement was
            // a 0.32 m ledge onto its verge, after WP-13 moved them 3 cm)
            return ledges && laid && prof[2].y - (stoppedBy - RoadsideRules.EdgeDropM) > RoadsideRules.LedgeStepM &&
                   Vector2.Distance(new Vector2(prof[2].x, prof[2].z), pW) > VertexSlackM;
        }

        /// <summary>Did the verge solve connect down to the road that stopped
        /// it (its last point at that road's verge, an inch under its
        /// pavement) at no more than RoadsideRules.TraversableSlope from the
        /// edge?</summary>
        static bool TraversableConnector(bool laid, float stoppedBy, Vector2 pW, float y, Vector3[] prof)
        {
            if (!laid) return false;
            float run = Vector2.Distance(new Vector2(prof[2].x, prof[2].z), pW);
            if (run < 0.5f) return false;
            return Mathf.Abs(prof[2].y - (stoppedBy - RoadsideRules.EdgeDropM)) <= 0.1f
                   && y - prof[2].y <= RoadsideRules.TraversableSlope * run + RoadsideRules.EdgeDropM;
        }

        /// <summary>The surface a given distance out from an edge: another
        /// road's pavement as drawn, if one is met on the way (ClearRun, over
        /// the roads GatherNear found), or the lattice.</summary>
        static float SurfaceOut(CityMap map, Trims trims, Vector2 pW, Vector2 outw, float y, float dist)
        {
            float run = ClearRun(map, trims, pW, outw, y, dist, out float otherY);
            if (run < dist) return otherY;
            var q = pW + outw * dist;
            return LatticeY(map, q.x, q.y);
        }

        static Vector3 tileOrigin;
        /// <summary>The lattice under a retaining face's foot, just outside it.</summary>
        static float RetainFootY(CityMap map, Section sec, int side, Vector3 ed)
        {
            var q = new Vector2(ed.x + tileOrigin.x, ed.z + tileOrigin.z) + sec.Out(side) * RailOverhangM;
            return LatticeY(map, q.x, q.y);
        }

        /// <summary>B6: a squeezed neighbour that is to draw the shared rail
        /// must verifiably draw it here — on structure, inside its trims, not
        /// clipped, not in a gore gap on the side facing us. Anything we
        /// cannot see, we draw ourselves.</summary>
        static bool NeighbourDrawsRail(CityMap map, Trims trims, CityMap.Edge e, Section sec, int side)
        {
            int oi = sec.Nb(side); float atO = sec.NbAt(side);
            var o = map.edges[oi];
            if (!o.ElevatedAt(atO)) return false;
            return NeighbourSideOpen(map, trims, e, sec, o, atO, out _);
        }

        static bool NeighbourDrawsBarrier(CityMap map, Trims trims, CityMap.Edge e, Section sec, int side)
        {
            int oi = sec.Nb(side); float atO = sec.NbAt(side);
            var o = map.edges[oi];
            if (!Barriered(o) || o.ElevatedAt(atO)) return false;
            // only its median barrier is unconditional; a cut wall is a run
            return NeighbourSideOpen(map, trims, e, sec, o, atO, out int sideO) && sideO > 0;
        }

        /// <summary>Does a median barrier stand on the squeeze strip beside
        /// this section's side — ours (a barriered road's median side,
        /// DecideSideFlags) or the neighbour's, drawn where we can see it?</summary>
        static bool StripBarriered(CityMap map, Trims trims, CityMap.Edge e, Section sec, int side) =>
            MedianBarriered(e, side) || NeighbourDrawsBarrier(map, trims, e, sec, side);

        /// <summary>Is this side a barriered road's median, which gets a
        /// Jersey barrier wherever it has no rail (DecideSideFlags)?</summary>
        static bool MedianBarriered(CityMap.Edge e, int side) => side > 0 && Barriered(e);

        static bool NeighbourSideOpen(CityMap map, Trims trims, CityMap.Edge e, Section sec, CityMap.Edge o, float atO, out int sideO)
        {
            sideO = 0;
            // ...and it must stand high enough to guard US: a barrier drawn on
            // a neighbour SharedGuardDyM lower is under our wheels (DropAt
            // puts our own rail on a retaining face there)
            if (o.YAt(atO) < e.YAt(sec.s) - SharedGuardDyM) return false;
            var p = e.PointAt(sec.s);
            var tO = o.TangentAt(atO);
            sideO = Vector2.Dot(p - o.PointAt(atO), new Vector2(-tO.y, tO.x)) >= 0f ? 1 : -1;
            if (atO < trims.atA[o.index] || atO > o.length - trims.atB[o.index]) return false;
            if (ClipAt(o, atO) != null) return false;
            return !InGoreGap(o.index, sideO, atO - 0.5f, atO + 0.5f);
        }

        static void EmitSide(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, int i, int side, float v0, float v1)
        {
            var A = sections[i - 1]; var B = sections[i];
            var f = spanFlags[i];
            var sf = f[side];
            var flare = side < 0 ? flareL : flareR;
            var shift = side < 0 ? shiftL : shiftR;
            var taper = side < 0 ? taperL : taperR;
            var outA = A.Out(side); var outB = B.Out(side);
            var eA = A.Edge(side); var eB = B.Edge(side);
            var fA = eA + new Vector3(outA.x, 0f, outA.y) * flare[i - 1];
            var fB = eB + new Vector3(outB.x, 0f, outB.y) * flare[i];
            float len = Vector2.Distance(new Vector2(eA.x, eA.z), new Vector2(eB.x, eB.z));
            if (groundLog != null) groundTag = $"e{e.index} '{e.name}' side {(side < 0 ? "L" : "R")} span {A.s:0.0}..{B.s:0.0}";

            // PLAN A2: a union side. Its owner draws the median from this edge
            // to the partner's (the slab, its soffit, curbs or a Jersey, and
            // the run's closed ends); the partner draws nothing here.
            if (sf.union)
            {
                var ur = UnionOn(trims, e.index, side, A.s, B.s);
                if (ur != null && ur.owner) EmitUnionMedian(map, trims, tm, e, i, side, v0, v1, f.elev, ur);
                return;
            }

            if (sf.rail)
            {
                float outAm = flare[i - 1] + Mathf.Max(shift[i - 1], VertexRailOut(e, A, side, tm.origin)),
                      outBm = flare[i] + Mathf.Max(shift[i], VertexRailOut(e, B, side, tm.origin));
                float drop = f.elev ? CityElevation.DeckThick : RailBuryM + 0.5f * Mathf.Max(outAm, outBm);
                // A flared or eased rail stands over the verge, which falls at
                // most 1V:6H past a 4% shoulder: half its offset buries its
                // traffic face's foot under that verge (it is under the tarmac
                // where the offset is still inside the edge).
                var rA = eA + new Vector3(outA.x, 0f, outA.y) * outAm;
                var rB = eB + new Vector3(outB.x, 0f, outB.y) * outBm;
                float sinkA = f.elev ? 0f : 0.5f * outAm, sinkB = f.elev ? 0f : 0.5f * outBm;
                // Over a squeeze strip, as far as the neighbour's drawn
                // pavement and no further (OverhangBeside): where that varies
                // along the span the rail is laid in quarters, its outer face
                // following the gap.
                // And not on another arm's pavement at a junction
                // (RailOverArms): those quarters are left out.
                bool shortOver = OverhangBeside(map, trims, tm, e, A, B, side, outAm, outBm);
                int armPieces = Mathf.Clamp(Mathf.CeilToInt(len / ArmSampleM), OverhangSamples - 1, MaxArmPieces);
                bool overArm = RailOverArms(map, trims, tm, e, A, B, side, rA, rB, -outA, -outB, armPieces);
                int pieces = overArm ? armPieces : shortOver ? OverhangSamples - 1 : 1;
                for (int q = 0; q < pieces; q++)
                {
                    if (overArm && railOverArm[q]) continue;
                    float t0 = (float)q / pieces, t1 = (float)(q + 1) / pieces;
                    var a = Vector3.Lerp(rA, rB, t0); var b = Vector3.Lerp(rA, rB, t1);
                    var ia = Vector2.Lerp(-outA, -outB, t0).normalized; var ib = Vector2.Lerp(-outA, -outB, t1).normalized;
                    float ovA = pieces == 1 ? RailOverhangM : OverhangAtT(t0), ovB = pieces == 1 ? RailOverhangM : OverhangAtT(t1);
                    bool capA = q == 0 ? sf.capStart : overArm && railOverArm[q - 1];
                    bool capB = q == pieces - 1 ? sf.capEnd : overArm && railOverArm[q + 1];
                    float tpA = Mathf.Lerp(taper[i - 1], taper[i], t0), tpB = Mathf.Lerp(taper[i - 1], taper[i], t1);
                    EmitRail(a, b, ia, ib, drop, capA, capB,
                             Mathf.Lerp(v0, v1, t0), Mathf.Lerp(v0, v1, t1),
                             Mathf.Lerp(sinkA, sinkB, t0), Mathf.Lerp(sinkA, sinkB, t1), ovA, ovB,
                             Mathf.Lerp(TaperFootM, RailH, tpA), Mathf.Lerp(TaperFootM, RailH, tpB));
                    railLog?.Add(new RailRecord
                    {
                        edge = e.index, side = side, node = -1, s0 = Mathf.Lerp(A.s, B.s, t0), s1 = Mathf.Lerp(A.s, B.s, t1),
                        a = a + tm.origin, b = b + tm.origin, inA = ia, inB = ib, overhang = Mathf.Min(ovA, ovB),
                    });
                }
                tm.railMetres += len;
            }
            // THE W-BEAM LEAD-IN (leftover item 2) over this span, if one reaches it
            if (!f.elev)
                for (int dir = -1; dir <= 1; dir += 2)
                {
                    var wbu = WbList(side, dir);
                    if (!float.IsNaN(wbu[i - 1]) && !float.IsNaN(wbu[i])) EmitWBeam(tm, A, B, side, wbu[i - 1], wbu[i]);
                }
            if (f.elev)
            {
                // THE GROUNDED END OF A SPAN DRAWN AS DECK. A structure end is a
                // station, so the span from it to the next sample — up to ten
                // metres of road on the ground — is drawn as deck, with the land
                // still dug for the soffit beside it: a ledge 1.06 m deep 5 cm
                // past the approach's edge, under its rail (e23419, s 37-47).
                // The approach rail stands over the fill a verge grades, as it
                // does on the next span.
                if (!GroundedDeckEnd(map, trims, tm, e, i, side))
                {
                    // THE SEAM BETWEEN TWO DECKS. A branch clipped against its
                    // host on structure meets it along a seam drawn from two
                    // sets of cross-sections, as on the ground (below), and it
                    // opened to a crack a few centimetres wide. On the filtered
                    // grid the land under an approach stood at the deck, so a
                    // ray through the crack met grass a centimetre down; on the
                    // real ground it met the land 1.75 m down, 30 m before the
                    // ramp e280 joins I-77 (e2132) on the Uptown Loop's tile —
                    // a hole for a wheel probe in the merge lane (WP-04 review).
                    // The same flush strip floors it, under the other deck —
                    // narrower: a seam on the ground lies under the verge
                    // where it runs out past the other surface, and one 0.6 m
                    // wide on a deck showed as grass past the ramp's rail
                    // where e280 folds into I-77.
                    if (sf.gap && !f.skip) EmitSeam(map, trims, tm, e, eA, eB, outA, outB, true);
                    return;
                }
                var approachVerge = new StripShape { shoulder = ShoulderOf(e, side), maxRun = VergeMaxRunM };
                EmitStrip(map, trims, tm, eA, eB, outA, outB, approachVerge);
                tm.vergeSpans.Add((e.index, side, A.s, B.s));
                tm.vergeMetres += len;
                return;
            }

            // the face under the edge: render-only, and only where no other
            // surface meets the edge (nor a retaining face stands in its plane).
            // A wedge span's OUTER side is a real edge: its collapsed end sits
            // on the host's edge exactly where the host's verge stands down.
            if (!sf.gap && !(sf.rail && sf.retain))
                kerbBucket.WallSloped(eA, eB, eA.y - KerbFaceM, eA.y, eB.y - KerbFaceM, eB.y,
                                      outA, v0, v1, 0f, 0.05f);

            if (sf.median) EmitBarrier(eA, eB, outA, v0, v1, sf.capStart, sf.capEnd,
                                       Mathf.Lerp(TaperFootM, BarrierH, taper[i - 1]), Mathf.Lerp(TaperFootM, BarrierH, taper[i]));

            if (sf.rail && sf.retain)
            {
                // Two roads that cannot be graded apart: the upper edge stands
                // on a retaining face down to the lattice. A WALL, so it goes
                // on the Solid layer with the rail above it: in the roads mesh
                // a car below that met it was touching road.
                float yA = RetainFootY(map, A, side, eA) - CityElevation.CorridorSink;
                float yB = RetainFootY(map, B, side, eB) - CityElevation.CorridorSink;
                barrierBucket.WallSloped(eA, eB, Mathf.Min(yA, eA.y - KerbFaceM), eA.y,
                                         Mathf.Min(yB, eB.y - KerbFaceM), eB.y, outA, v0, v1, 0f, 0.15f);
                return;
            }

            if (sf.cut && !sf.rail)
            {
                tm.cutWallM += Vector3.Distance(fA, fB);
                tm.cutWallByWhy[Mathf.Min(sf.cutWhy, (byte)3)] += Vector3.Distance(fA, fB);
                {
                    float cwm = Vector3.Distance(fA, fB);
                    if (tm.cutWallByEdge.TryGetValue(e.index, out var cw)) cwm += cw.m;
                    tm.cutWallByEdge[e.index] = (cwm, sf.cutWhy, new Vector2(fA.x + tm.origin.x, fA.z + tm.origin.z));
                }
                EmitBarrier(fA, fB, outA, v0, v1, sf.capStart, sf.capEnd);
                // A flared end stands its face off the edge, and there was
                // nothing between the two but the lattice a sink and more
                // below: a slot up to 0.8 m wide along the last twelve metres
                // of every cut wall (a 20-60 cm lip on I-277 through uptown).
                // It is floored flush with the verge's first inch, out under
                // the barrier's foot.
                if (flare[i - 1] > 0f || flare[i] > 0f)
                {
                    var o = tm.origin;
                    var dn = Vector3.down * RoadsideRules.EdgeDropM;
                    var under = BarrierW * 0.5f;
                    StripQuad(map, tm, eA + o + dn, fA + Flat(outA) * under + o + dn,
                              fB + Flat(outB) * under + o + dn, eB + o + dn);
                }
                // THE GROUND BEHIND A RETAINING WALL IS AT ITS TOP. It was a
                // flat shelf 20 cm under the tarmac out to hw + 11 m, and a car
                // through any gap drove along behind the wall; the shelf is
                // sealed under solid ground now, and the run's ends close it.
                var shelf = new StripShape { start = BarrierW, rise = BarrierH, flat = true, maxRun = CutShelfMaxM };
                EmitStrip(map, trims, tm, fA, fB, outA, outB, shelf);
                if (sf.capStart) EmitSeal(map, trims, tm, fA, outA, shelf, -e.TangentAt(A.s));
                if (sf.capEnd) EmitSeal(map, trims, tm, fB, outB, shelf, e.TangentAt(B.s));
                return;
            }

            if (sf.gap)
            {
                // Another surface owns this edge — the branch beside its host,
                // the gore, the host under a clipped branch — but the two are
                // drawn from different cross-sections (a mitred node, a clip
                // cut along the other road's tangent, a gore chord), and the
                // seam between them opened to a hand's width in places with the
                // lattice under it. A flush strip under the seam floors it; it
                // lies under the other surface everywhere else.
                if (!f.skip) EmitSeam(map, trims, tm, e, eA, eB, outA, outB, false);
                return;
            }
            float strip = Mathf.Max(A.Strip(side), B.Strip(side));
            if (strip >= 0f)
            {
                // squeezed against a neighbour: floor our half of the strip
                var half = new StripShape { flat = true, fixedRun = true, halfGap = true, maxRun = strip * 0.5f, rise = -RoadsideRules.EdgeDropM };
                EmitStrip(map, trims, tm, eA, eB, outA, outB, half);
                return;
            }
            float shoulder = ShoulderOf(e, side);
            // Behind a rail the verge is the whole graded roadside too — the
            // fill slope an approach guardrail stands on. It used to stop past
            // the rail's foot and drop straight down to the lattice there.
            var verge = new StripShape { shoulder = shoulder, maxRun = VergeMaxRunM };
            EmitStrip(map, trims, tm, eA, eB, outA, outB, verge);
            tm.vergeSpans.Add((e.index, side, A.s, B.s));
            tm.vergeMetres += len;
        }

        /// <summary>A flush strip under a gap side's seam with the surface
        /// that owns the edge (EmitSide), an inch down and falling away under
        /// that surface (SeamCeiling): GapSeamM wide on the ground,
        /// DeckSeamM on a deck (<paramref name="onDeck"/>).</summary>
        static void EmitSeam(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, Vector3 eA, Vector3 eB, Vector2 outA, Vector2 outB, bool onDeck)
        {
            var seam = new StripShape { flat = true, fixedRun = true, seam = true, deckSeam = onDeck, ownEdge = e.index,
                                        maxRun = onDeck ? DeckSeamM : GapSeamM, rise = -RoadsideRules.EdgeDropM };
            EmitStrip(map, trims, tm, eA, eB, outA, outB, seam);
        }

        /// <summary>
        /// How far a rail's solid may reach past the edge it stands on, over a
        /// squeeze strip: RailOverhangM (it floors the strip between two decks
        /// and closes the slot at a retaining face's foot), but never over the
        /// neighbour's pavement as DRAWN. Two squeezed ribbons keep their
        /// 0.3 m strip only at their own cross-sections, which are ten metres
        /// apart and not at the same places; between them each is a chord, and
        /// where the split between the two roads varies, one ribbon's edge ran
        /// up to 0.4 m into the other's. A rail's overhang, reaching the full
        /// 0.3 m past its edge, stood in the other road's outside lane there:
        /// the lane survey found 21 such runs after WP-04 (e1913's rail under
        /// I-277's e1912, e2309's beside e2303 on the Uptown Loop, e15185's
        /// under the link e20364). The gap to the neighbour's drawn pavement is
        /// read along the rail's own line at its ends and the three quarter
        /// points between (<see cref="overhangAt"/>), and where any is short of
        /// RailOverhangM the caller lays the rail in quarters, each reaching
        /// that far. (One least overhang for the whole span left the strip
        /// open to the lattice where the gap widened again: a 0.85 m lip
        /// beside e1896's retaining face over the Tyvola ramp e328.) False
        /// where no sample is short.
        ///
        /// Nor over ANY other road's drawn pavement a car could be on under
        /// it: one whose surface there is within a car's height
        /// (RoadsideRules.CarBandM) below the rail's foot, or no higher than
        /// its top. Two roads drawn into each other at two heights put the
        /// upper one's rail over the lower one's lanes, the overhang half a
        /// metre in: Albemarle Road's deck rail (e2300) over the Independence
        /// Expressway's right lane (e2291, 0.5 m lower, on the Independence
        /// route) and East 12th Street's approach rail (e11146) over the link
        /// e11144 (0.9 m lower), both after WP-04's ground, and before it US
        /// 74's over I-277's left lane on the Uptown Loop (e2367 over e2321).
        /// A deck over a road passes more than a car's height over it and
        /// keeps its overhang.
        /// </summary>
        static bool OverhangBeside(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, Section A, Section B, int side, float outAm, float outBm)
        {
            for (int q = 0; q < OverhangSamples; q++) overhangAt[q] = RailOverhangM;
            int nbA = A.Nb(side), nbB = B.Nb(side);
            bool shorter = false;
            var o = tm.origin;
            Vector2 outA = A.Out(side), outB = B.Out(side);
            var pA = new Vector2(A.Edge(side).x + o.x, A.Edge(side).z + o.z) + outA * outAm;
            var pB = new Vector2(B.Edge(side).x + o.x, B.Edge(side).z + o.z) + outB * outBm;
            for (int k = 0; k < 2; k++)
            {
                int nb = k == 0 ? nbA : nbB;
                if (nb < 0 || (k == 1 && nb == nbA)) continue;
                var ol = OutlineOf(map, trims, nb);
                if (ol.L == null) continue;
                for (int q = 0; q < OverhangSamples; q++)
                {
                    float t = (float)q / (OverhangSamples - 1);
                    var u = Vector2.Lerp(outA, outB, t);
                    if (u.sqrMagnitude < 1e-6f) continue;
                    u.Normalize();
                    float g = PavementAlong(ol, Vector2.Lerp(pA, pB, t), u, overhangAt[q]);
                    if (g < overhangAt[q] - 1e-3f) { overhangAt[q] = Mathf.Max(0f, g); shorter = true; }
                }
            }
            // every other road within reach, by the height band
            GatherNear(map, pA, pB, RailOverhangM);
            GatherPavement(map, trims);
            float yA = A.Edge(side).y + o.y, yB = B.Edge(side).y + o.y;
            var bMin = Vector2.Min(pA, pB) - Vector2.one * RailOverhangM;
            var bMax = Vector2.Max(pA, pB) + Vector2.one * RailOverhangM;
            for (int k = 0; k < nearEdges.Count; k++)
            {
                int oi = nearEdges[k];
                if (oi == e.index || oi == nbA || oi == nbB) continue;
                var box = nearEdgeBox[k];
                if (bMax.x < box.x || bMin.x > box.z || bMax.y < box.y || bMin.y > box.w) continue;
                var ol = OutlineOf(map, trims, oi);
                if (ol.L == null || bMax.x < ol.minX || bMin.x > ol.maxX || bMax.y < ol.minZ || bMin.y > ol.maxZ) continue;
                for (int q = 0; q < OverhangSamples; q++)
                {
                    float t = (float)q / (OverhangSamples - 1);
                    var u = Vector2.Lerp(outA, outB, t);
                    if (u.sqrMagnitude < 1e-6f) continue;
                    u.Normalize();
                    float y = Mathf.Lerp(yA, yB, t);
                    float g = PavementAlong(ol, Vector2.Lerp(pA, pB, t), u, overhangAt[q], y - RoadsideRules.CarBandM, y + RailH);
                    if (g < overhangAt[q] - 1e-3f) { overhangAt[q] = Mathf.Max(0f, g); shorter = true; }
                }
            }
            return shorter;
        }
        /// <summary>Where OverhangBeside reads the gap: the span's ends and
        /// the three quarter points between.</summary>
        const int OverhangSamples = 5;
        static readonly float[] overhangAt = new float[OverhangSamples];

        /// <summary>
        /// Which pieces of a rail's run over a span stand on ANOTHER ROAD'S
        /// pavement at a junction (<see cref="railOverArm"/>, by piece of
        /// <paramref name="pieces"/>, about <see cref="ArmSampleM"/> each;
        /// true where any does): a node's other arms, host and branch
        /// included, and the node's fan. A piece is left out, and the rail
        /// capped at it, where at its middle the rail's outer face stands
        /// over such pavement (its back is to another road: there is no fall
        /// behind it), or both its line and its traffic face do (it stands in
        /// the other road), that pavement no more than a ledge (LedgeStepM)
        /// under the rail's foot and no higher than its top. A squeezed
        /// neighbour behind the rail does not count for the outer face: the
        /// strip between them is the shared rail's (OverhangBeside).
        ///
        /// Two arms of one node meeting at an angle are drawn into each other
        /// short of the fan, and a short arm's trim is capped at half its
        /// length, so its mouth can stand inside the next arm's footprint.
        /// Elizabeth Avenue's ten metres over Little Sugar Creek (e9267) are a
        /// deck on the 3DEP ground, trimmed 4.9 m from node 6995 with a 7.6 m
        /// half width, 0.25 m over North Kings Drive there, and its deck rail
        /// stood 1.2 m tall across Kings Drive's left lane inside the fan, a
        /// metre from Kings Drive's mouth (the fan mouth probe's "a solid",
        /// WP-04 review). The first cut of this rule read one point per
        /// quarter span, the rail's line 0.3 m inside the other arm's ribbon,
        /// and not the fan: it found nothing there; the deck's edge runs
        /// along the fan's side, and only the rail's outer face is over it
        /// where the probe met the rail. On Tyvola Road's bridge
        /// the rails of the host's pieces e14953 and e2726 ran on across the
        /// lanes of the ramps clipped into them (e14947, e14950) for a metre
        /// and a half next to the node, where the gore gap did not reach that
        /// piece and the ramp's pavement lay behind the rail; a host and
        /// branch are arms like any other here, and a quarter span left out
        /// there would have opened more of the gore's edge over I-77 than the
        /// ramp covers, so the rail is cut at half-metre pieces. And a road's
        /// rail where a neighbour arm stands a third of a metre ABOVE it
        /// (North Davidson Street's e14102 beside e14106 at node 13275)
        /// guards no fall but put 0.6 m of rail into the upper road's lane.
        /// </summary>
        static bool RailOverArms(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, Section A, Section B, int side,
                                 Vector3 rA, Vector3 rB, Vector2 inA, Vector2 inB, int pieces)
        {
            for (int q = 0; q < pieces; q++) railOverArm[q] = false;
            bool any = false;
            // a squeezed neighbour's pavement behind the rail is the strip's
            // other side (OverhangBeside and the shared rail own that)
            int nbA = A.Nb(side), nbB = B.Nb(side);
            if (armLog != null && armLogEdges.Contains(e.index))
                armLog.Add($"e{e.index} span {A.s:0.00}..{B.s:0.00} of {e.length:0.0}, {pieces} pieces, rail ({rA.x + tm.origin.x:0.00},{rA.z + tm.origin.z:0.00})-({rB.x + tm.origin.x:0.00},{rB.z + tm.origin.z:0.00}), nodes {e.a}/{e.b}");
            for (int end = 0; end < 2; end++)
            {
                int node = end == 0 ? e.a : e.b;
                if (end == 1 && node == e.a) break;
                if ((end == 0 ? A.s : e.length - B.s) > ArmOverlapReachM) continue;
                bool fan = trims.patch[node] && FanPolyOf(map, trims, node).tris != null;
                for (int q = 0; q < pieces; q++)
                {
                    if (railOverArm[q]) continue;
                    float t = (q + 0.5f) / pieces;
                    var p3 = Vector3.Lerp(rA, rB, t) + tm.origin;
                    var line = new Vector2(p3.x, p3.z);
                    var inw = Vector2.Lerp(inA, inB, t);
                    if (inw.sqrMagnitude < 1e-6f) continue;
                    inw.Normalize();
                    var face = line + inw * RailW;
                    var outer = line - inw * RailOverhangM;
                    float lo = p3.y - RoadsideRules.LedgeStepM, hi = p3.y + RailH;
                    bool onLine = OnArmOrFan(map, trims, e, node, fan, line, lo, hi, -1, -1),
                         onFace = onLine && OnArmOrFan(map, trims, e, node, fan, face, lo, hi, -1, -1),
                         onOuter = OnArmOrFan(map, trims, e, node, fan, outer, lo, hi, nbA, nbB);
                    if (armLog != null && armLogEdges.Contains(e.index))
                        armLog.Add($"e{e.index} s {Mathf.Lerp(A.s, B.s, t):0.00} node {node}{(fan ? " fan" : "")}: line ({line.x:0.00},{line.y:0.00}) y {p3.y:0.00} {(onLine ? "ON" : "off")}{DescribeArmOrFan(map, trims, e, node, fan, line)}; " +
                                   $"face {(onFace ? "ON" : "off")}{DescribeArmOrFan(map, trims, e, node, fan, face)}; outer {(onOuter ? "ON" : "off")}{DescribeArmOrFan(map, trims, e, node, fan, outer)}");
                    if ((onLine && onFace) || onOuter)
                    {
                        railOverArm[q] = true; any = true;
                    }
                }
            }
            return any;
        }
        /// <summary>Null in a build. A probe that sets it (and names edges in
        /// <see cref="armLogEdges"/>) gets RailOverArms' reading of every
        /// piece of those edges' rails.</summary>
        public static List<string> armLog;
        public static readonly HashSet<int> armLogEdges = new HashSet<int>();
        /// <summary>For armLog: what pavement a point is on, and at what height.</summary>
        static string DescribeArmOrFan(CityMap map, Trims trims, CityMap.Edge e, int node, bool fan, Vector2 q)
        {
            var sb = new System.Text.StringBuilder();
            if (fan)
            {
                var T = fanPolys[node].tris;
                for (int i = 0; i + 2 < T.Length; i += 3)
                    if (TriInterval(T[i], T[i + 1], T[i + 2], q, Vector2.right, 0f, out _, out _)) { sb.Append($" [fan y {TriHeight(T[i], T[i + 1], T[i + 2], q):0.00}]"); break; }
            }
            foreach (int oi in map.nodeEdges[node])
            {
                if (oi == e.index) continue;
                var ol = OutlineOf(map, trims, oi);
                if (ol.L == null) { sb.Append($" [e{oi} no outline]"); continue; }
                for (int i = 1; i < ol.L.Length; i++)
                {
                    Vector3 aL = ol.L[i - 1], bL = ol.L[i], bR = ol.R[i], aR = ol.R[i - 1];
                    if (TriInterval(aL, bL, bR, PavedInsetM, 0f, 0f, q, Vector2.right, 0f, out _, out _)) { sb.Append($" [e{oi} y {TriHeight(aL, bL, bR, q):0.00}]"); break; }
                    if (TriInterval(aL, bR, aR, 0f, PavedInsetM, 0f, q, Vector2.right, 0f, out _, out _)) { sb.Append($" [e{oi} y {TriHeight(aL, bR, aR, q):0.00}]"); break; }
                }
            }
            return sb.ToString();
        }
        /// <summary>The length of rail RailOverArms reads and leaves out at a
        /// time, and the most pieces a span is cut into.</summary>
        const float ArmSampleM = 0.5f;
        const int MaxArmPieces = 64;
        static readonly bool[] railOverArm = new bool[MaxArmPieces];
        /// <summary>How far from a node a span's rail is checked against the
        /// node's other arms: past a trim and a half width.</summary>
        const float ArmOverlapReachM = 30f;

        /// <summary>OverhangBeside's reach at a fraction of the span, between
        /// its five samples.</summary>
        static float OverhangAtT(float t)
        {
            float u = Mathf.Clamp01(t) * (OverhangSamples - 1);
            int i = Mathf.Min((int)u, OverhangSamples - 2);
            return Mathf.Lerp(overhangAt[i], overhangAt[i + 1], u - i);
        }

        /// <summary>Is a point (world plan) on the fan of <paramref name="node"/>
        /// or on the drawn pavement of one of its arms other than
        /// <paramref name="e"/>, with the surface there between
        /// <paramref name="yLo"/> and <paramref name="yHi"/>?</summary>
        static bool OnArmOrFan(CityMap map, Trims trims, CityMap.Edge e, int node, bool fan, Vector2 q, float yLo, float yHi, int notA, int notB)
        {
            if (fan && OnFan(fanPolys[node], q, yLo, yHi)) return true;
            foreach (int oi in map.nodeEdges[node])
            {
                if (oi == e.index || oi == notA || oi == notB) continue;
                var ol = OutlineOf(map, trims, oi);
                if (ol.L != null && OnPavement(ol, q, yLo, yHi, PavedInsetM)) return true;
            }
            return false;
        }

        /// <summary>Is a point (world plan) on a ribbon's drawn pavement, at
        /// least <paramref name="inset"/> inside its sides, with the surface
        /// there between <paramref name="yLo"/> and <paramref name="yHi"/>?</summary>
        static bool OnPavement(Outline ol, Vector2 q, float yLo, float yHi, float inset)
        {
            if (q.x < ol.minX || q.x > ol.maxX || q.y < ol.minZ || q.y > ol.maxZ) return false;
            for (int i = 1; i < ol.L.Length; i++)
            {
                Vector3 aL = ol.L[i - 1], bL = ol.L[i], bR = ol.R[i], aR = ol.R[i - 1];
                if (TriInterval(aL, bL, bR, inset, 0f, 0f, q, Vector2.right, 0f, out _, out _))
                {
                    float h = TriHeight(aL, bL, bR, q);
                    if (h >= yLo && h <= yHi) return true;
                }
                if (TriInterval(aL, bR, aR, 0f, inset, 0f, q, Vector2.right, 0f, out _, out _))
                {
                    float h = TriHeight(aL, bR, aR, q);
                    if (h >= yLo && h <= yHi) return true;
                }
            }
            return false;
        }

        /// <summary>Is a point (world plan) on a fan's pavement, its surface
        /// there between <paramref name="yLo"/> and <paramref name="yHi"/>?</summary>
        static bool OnFan(FanPoly fan, Vector2 q, float yLo, float yHi)
        {
            if ((q - fan.centre).sqrMagnitude > fan.reach * fan.reach) return false;
            var T = fan.tris;
            for (int i = 0; i + 2 < T.Length; i += 3)
            {
                if (!TriInterval(T[i], T[i + 1], T[i + 2], q, Vector2.right, 0f, out _, out _)) continue;
                float h = TriHeight(T[i], T[i + 1], T[i + 2], q);
                if (h >= yLo && h <= yHi) return true;
            }
            return false;
        }

        /// <summary>How far along a ray (world plan) a ribbon's drawn outline
        /// is first met, up to <paramref name="limit"/>; 0 where the ray
        /// starts on it.</summary>
        static float PavementAlong(Outline ol, Vector2 p, Vector2 dir, float limit)
            => PavementAlong(ol, p, dir, limit, float.NegativeInfinity, float.PositiveInfinity);

        /// <summary>The same, counting only pavement whose surface where the
        /// ray meets it is between <paramref name="yLo"/> and
        /// <paramref name="yHi"/>.</summary>
        static float PavementAlong(Outline ol, Vector2 p, Vector2 dir, float limit, float yLo, float yHi)
        {
            var far = p + dir * limit;
            if (Mathf.Max(p.x, far.x) < ol.minX || Mathf.Min(p.x, far.x) > ol.maxX ||
                Mathf.Max(p.y, far.y) < ol.minZ || Mathf.Min(p.y, far.y) > ol.maxZ) return limit;
            bool band = !float.IsNegativeInfinity(yLo) || !float.IsPositiveInfinity(yHi);
            float best = limit;
            for (int i = 1; i < ol.L.Length; i++)
            {
                if (ol.BlockMisses(i, Mathf.Min(p.x, far.x), Mathf.Min(p.y, far.y), Mathf.Max(p.x, far.x), Mathf.Max(p.y, far.y))) { i += Outline.Block - 1; continue; }
                Vector3 aL = ol.L[i - 1], bL = ol.L[i], bR = ol.R[i], aR = ol.R[i - 1];
                if (TriInterval(aL, bL, bR, p, dir, limit, out float t0, out float t1) && t1 - t0 > 1e-4f && t0 < best)
                {
                    float h = band ? TriHeight(aL, bL, bR, p + dir * (0.5f * (t0 + t1))) : 0f;
                    if (!band || (h >= yLo && h <= yHi)) best = t0;
                }
                if (TriInterval(aL, bR, aR, p, dir, limit, out t0, out t1) && t1 - t0 > 1e-4f && t0 < best)
                {
                    float h = band ? TriHeight(aL, bR, aR, p + dir * (0.5f * (t0 + t1))) : 0f;
                    if (!band || (h >= yLo && h <= yHi)) best = t0;
                }
            }
            return best;
        }

        /// <summary>
        /// How far out a rail stands at a polyline vertex, past the corner the
        /// ribbon draws there. The section at a vertex is cut along the
        /// bisector at the plain half width (see RightAt), so its corner lies
        /// hw (1 - cos) inside each segment's own edge line, and the rail RailW
        /// inside that corner stood that much further into the lane on a bridge
        /// approach's bends: 0.46 m in from Bryant Street's edge (e21604), a
        /// hand past the lane line over seven metres of e23419 (2026-09-14). At
        /// the vertex it moves out along the bisector to where it would stand
        /// off the segments' edge lines, no further than half its width, so its
        /// traffic face stays over the deck a car is on (a flare's slot is what
        /// a rail off a deck edge leaves). Not on a side squeezed against a
        /// neighbour: the strip is the neighbour's too.
        /// </summary>
        static float VertexRailOut(CityMap.Edge e, Section sec, int side, Vector3 origin)
        {
            if (sec.Strip(side) >= 0f || sec.collapsed) return 0f;
            int k = System.Array.BinarySearch(e.s, sec.s);
            if (k <= 0 || k >= e.s.Length - 1) return 0f;
            Vector2 t0 = (e.pts[k] - e.pts[k - 1]).normalized, t1 = (e.pts[k + 1] - e.pts[k]).normalized;
            var bis = t0 + t1;
            if (bis.sqrMagnitude < 1e-6f) return 0f;
            float cosHalf = Vector2.Dot(bis.normalized, t0);
            var edge = sec.Edge(side);
            float hw = Vector2.Distance(new Vector2(edge.x + origin.x, edge.z + origin.z), e.pts[k]);
            if (cosHalf < 0.5f) return RailVertexOutMaxM;
            return Mathf.Min(hw * (1f / cosHalf - 1f), RailVertexOutMaxM);
        }

        const float RailVertexOutMaxM = RailW * 0.5f;

        /// <summary>Does span <paramref name="i"/>'s side on a span drawn as
        /// deck get a verge (EmitSide)? Only the grounded end of one: one
        /// section on structure, the span's middle on the ground, a side of
        /// its own (no gap, wedge or squeeze strip), and land beside its
        /// grounded section that a verge can grade. One rule for the tile and
        /// for DescribeSide's label.</summary>
        static bool GroundedDeckEnd(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, int i, int side)
        {
            var A = sections[i - 1]; var B = sections[i];
            if (A.elev == B.elev || spanFlags[i][side].gap || A.collapsed || B.collapsed || Mathf.Max(A.Strip(side), B.Strip(side)) >= 0f) return false;
            if (e.ElevatedAt(0.5f * (A.s + B.s))) return false;
            return (DropAt(map, trims, tm, e, A.elev ? i : i - 1, side) & DropUngraded) == 0;
        }

        /// <summary>
        /// Two roads running side by side closer than their widths — I-77's
        /// general lanes beside its express lanes, a frontage road beside a
        /// freeway, the two carriageways of a divided road mapped tight —
        /// used to overlap, and each one's barrier stood inside the other's
        /// lane. The space between the centrelines is now split between
        /// them in proportion to their widths, and where the strip left is
        /// narrower than a barrier the two share one (the lower-numbered edge
        /// draws it). Host and branch pairs are excluded: they overlap by
        /// design and the clip owns them.
        /// </summary>
        static readonly HashSet<int> nbScratch = new HashSet<int>();
        static void SqueezeSection(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e,
                                   Vector2 p, Vector2 right, float hw, float y, ref Section sec)
        {
            if (sec.collapsed) return;
            float hwL = Vector2.Distance(new Vector2(sec.L.x + tm.origin.x, sec.L.z + tm.origin.z), p);
            float hwR = Vector2.Distance(new Vector2(sec.R.x + tm.origin.x, sec.R.z + tm.origin.z), p);
            float sqL = hwL, sqR = hwR;
            Squeeze(map, trims, e, p, right, hw, y, ref sqL, ref sqR,
                    out sec.nbL, out sec.nbR, out sec.nbAtL, out sec.nbAtR, out sec.stripL, out sec.stripR);
            if (sqR < hwR - 1e-3f)
            { sec.R = new Vector3(p.x + right.x * sqR - tm.origin.x, y, p.y + right.y * sqR - tm.origin.z); sec.sqR = true; }
            if (sqL < hwL - 1e-3f)
            { sec.L = new Vector3(p.x - right.x * sqL - tm.origin.x, y, p.y - right.y * sqL - tm.origin.z); sec.sqL = true; }
        }

        /// <summary>The ribbon's half width each side at an arc position —
        /// taper and squeeze applied — which is what the tile draws and what
        /// the drive audit must probe. (The audit's cross ray once ran to the
        /// nominal edge and reported every squeezed barrier as a wall.)</summary>
        public static void LaneExtents(CityMap map, Trims trims, CityMap.Edge e, float s, out float hwL, out float hwR)
        {
            // after a tile build, the ribbon it drew: its sections, as they
            // stand (the eased squeeze, a clip, a taper), straight between
            // them as the mesh is - so an audit probes the edge the tile drew
            if (!inBuild && EaseSqueezes && rawSectionCache.TryGetValue(e.index, out var secs) && secs.Count >= 2
                && s >= secs[0].s && s <= secs[secs.Count - 1].s)
            {
                int lo = 0, hi = secs.Count - 1;
                while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (secs[mid].s <= s) lo = mid; else hi = mid; }
                var A = secs[lo]; var B = secs[hi];
                float t = B.s - A.s > 1e-5f ? (s - A.s) / (B.s - A.s) : 0f;
                if (A.collapsed && B.collapsed) { hwL = 0f; hwR = 0f; return; }
                float LatW(in Section c, Vector3 v) => Vector2.Dot(new Vector2(v.x, v.z) - c.P, c.right);
                hwL = -Mathf.Lerp(LatW(A, A.L), LatW(B, B.L), t);
                hwR = Mathf.Lerp(LatW(A, A.R), LatW(B, B.R), t);
                return;
            }
            var p = e.PointAt(s);
            var tan = e.TangentAt(s);
            var right = new Vector2(-tan.y, tan.x);
            // squeezed and clipped about the ribbon's own centre (the line
            // model), then returned off the OSM line like every extent
            LineModel.CentreAt(e, s, out float c, out float hw);
            var pc = p + right * c;
            float hl = hw, hr = hw;
            Squeeze(map, trims, e, pc, right, hw, e.YAt(s), ref hl, ref hr, out _, out _, out _, out _, out _, out _);
            if (EaseSqueezes && !inBuild)
            {
                // the eased squeeze (EaseSqueeze), where the tile's sections
                // are not to hand
                hl = Mathf.Min(hl, hw - EasedCut(map, trims, e, s, -1));
                hr = Mathf.Min(hr, hw - EasedCut(map, trims, e, s, 1));
            }
            // a clipped branch: its inner edge is the host's edge (or nothing at all)
            if (extentDepth <= 2)   // two edges each the other's host: stop at the second level
            {
                extentDepth++;
                try { ClipExtents(map, trims, e, s, pc, right, hw, ref hl, ref hr); }
                finally { extentDepth--; }
            }
            if (hl <= 0f && hr <= 0f) { hwL = 0f; hwR = 0f; return; }   // collapsed: nothing of its own
            hwL = hl - c; hwR = hr + c;
        }
        static int extentDepth;
        static void ClipExtents(CityMap map, Trims trims, CityMap.Edge e, float s, Vector2 p, Vector2 right, float hw,
                                ref float hwL, ref float hwR)
        {
            var clip = ClipAt(e, s);
            if (clip == null) return;
            if (!CutAgainstHost(map, trims, e, s, p, right, hw, e.YAt(s), clip, out float lam, out bool collapsed)) return;
            if (collapsed) { hwL = 0f; hwR = 0f; return; }
            // where ClipSection puts the inner vertex, which may lie past the
            // ribbon's centre (a clip across a one-sided taper): never past the
            // other edge
            if (clip.innerSide > 0) hwR = Mathf.Clamp(lam, -hwL, hwR); else hwL = Mathf.Clamp(-lam, -hwR, hwL);
        }

        /// <summary>
        /// Two arms of one junction that the squeeze splits like any two
        /// roads: more than <see cref="ArmSplitDyM"/> apart in height, and
        /// beside each other where the other arm is a ribbon (past its trim;
        /// inside it the fan is the pavement). Arms were never split, and
        /// where a link leaves a node beside one of the roads it joins, the
        /// two ran on drawn into each other: the link e14103 half a metre over
        /// North Davidson Street e14105 from node 11493, each one's approach
        /// rail standing in the other's outside lane for twenty metres
        /// (2026-09-14). Two arms at one height drawn into each other are one
        /// pavement, as they always were (a harness census: 194 km of such
        /// sides city-wide within a seam of each other, 2.4 km further
        /// apart). A host and its branch are the clip's, as before. A fan's
        /// corners are its arms' nominal widths, so no fan moves; the ribbons
        /// split from the trim on (780 m narrowed city-wide, on 44 edges).
        /// </summary>
        static bool ArmsApart(CityMap map, Trims trims, CityMap.Edge e, Vector2 p, CityMap.Edge o, float atO, float y)
        {
            if (Mathf.Abs(o.YAt(atO) - y) <= ArmSplitDyM) return false;
            if (clipPairs.Contains(PairKey(e.index, o.index)) || zonePairs.Contains(PairKey(e.index, o.index))) return false;
            // the node they share nearer this station (two carriageways can share both)
            int n = -1;
            float nearest = float.MaxValue;
            if (e.a == o.a || e.a == o.b) { n = e.a; nearest = (map.nodes[e.a] - p).sqrMagnitude; }
            if ((e.b == o.a || e.b == o.b) && (map.nodes[e.b] - p).sqrMagnitude < nearest) n = e.b;
            float fromO = o.a == n ? atO : o.length - atO;
            return fromO >= trims.TrimAt(o, n) - VertexSlackM;
        }

        /// <summary>The height apart past which two arms of one junction are
        /// not one pavement: the overlap census's seam (CityAudit's MinDy).</summary>
        const float ArmSplitDyM = 0.25f;

        /// <summary>Edges with a deck side that stood no parapet against a
        /// same-way deck squeezed beside it (SameWayDeckBeside), for the
        /// audit. Cleared by the caller.</summary>
        public static readonly HashSet<int> sameWayDeckSides = new HashSet<int>();

        /// <summary>The squeezed neighbour on <paramref name="side"/> of a
        /// section carries traffic the same way as <paramref name="e"/> (both
        /// one-way, running within 25 degrees), stands on structure there too,
        /// and at this deck's height within a flush strip's step (0.10 m).</summary>
        static bool SameWayDeckBeside(CityMap map, CityMap.Edge e, in Section sec, int side)
        {
            int nb = sec.Nb(side);
            if (nb < 0) return false;
            var o = map.edges[nb];
            if (!e.oneway || !o.oneway || o.a == o.b) return false;
            float at = Mathf.Clamp(sec.NbAt(side), 0f, o.length);
            if (Vector2.Dot(e.TangentAt(sec.s), o.TangentAt(at)) < 0.9f) return false;
            if (!o.ElevatedAt(at)) return false;
            return Mathf.Abs(e.YAt(sec.s) - o.YAt(at)) <= 0.10f;
        }

        /// <summary>The longest edge that is only a connector between two
        /// pieces of one carriageway (OSM splits a way at a bridge's end, a
        /// lane drop, a tag change, and leaves a stub of a metre or two).</summary>
        public const float CarryOnLinkM = 3f;

        /// <summary>PSX_CITY_SQUEEZE_CARRYON=1: the squeeze as it was before
        /// 2026-10-04 (a road squeezed against the carriageway it carries on
        /// from through a short connector) - for a BEFORE count only.</summary>
        public static readonly bool SqueezeCarryOn = System.Environment.GetEnvironmentVariable("PSX_CITY_SQUEEZE_CARRYON") == "1";

        /// <summary>Edges whose squeeze passed over the road they carry on
        /// from (CarriesOnThrough) - the audit's count of what the rule
        /// changed. Cleared by the caller.</summary>
        public static readonly HashSet<int> carryOnSkipped = new HashSet<int>();

        /// <summary>Does <paramref name="o"/> carry on into <paramref name="e"/>
        /// (or e into o) through a connector edge shorter than
        /// <see cref="CarryOnLinkM"/>: one carriageway in three OSM pieces,
        /// never two roads side by side. (Two edges sharing a node are the
        /// arms' rule, ArmsApart.)</summary>
        static bool CarriesOnThrough(CityMap map, CityMap.Edge e, CityMap.Edge o)
        {
            for (int k = 0; k < 2; k++)
            {
                int n = k == 0 ? e.a : e.b;
                foreach (int ci in map.nodeEdges[n])
                {
                    if (ci == e.index || ci == o.index) continue;
                    var c = map.edges[ci];
                    if (c.a == c.b || c.length >= CarryOnLinkM) continue;
                    int m = c.a == n ? c.b : c.a;
                    if (o.a == m || o.b == m) return true;
                }
            }
            return false;
        }

        /// <summary>How far off a segment's end, per metre beside it, a point
        /// past the outside of a bend may be and still be squeezed against
        /// the vertex (Squeeze): tan 26.6 degrees, as sharp a bend as two
        /// roads the squeeze calls parallel (within 25.8 degrees) can make.</summary>
        const float OutsideBendOverM = 0.5f;

        static void Squeeze(CityMap map, Trims trims, CityMap.Edge e, Vector2 p, Vector2 right, float hw, float y,
                            ref float hwL, ref float hwR, out int nbL, out int nbR,
                            out float nbAtL, out float nbAtR, out float stripL, out float stripR)
        {
            nbL = -1; nbR = -1; nbAtL = 0f; nbAtR = 0f; stripL = -1f; stripR = -1f;
            nbScratch.Clear();
            // a segment further than 32 m is past any two half widths (13.1 m at most each)
            map.EdgeSegsNear(p - Vector2.one * 32f, p + Vector2.one * 32f, nbScratch);
            var tan = new Vector2(right.y, -right.x);
            // the nearest parallel road on each side, whatever it is
            float dL = float.MaxValue, dR = float.MaxValue;
            int eL = -1, eR = -1; float atL = 0f, atR = 0f;
            foreach (var packed in nbScratch)
            {
                int oi = packed >> 12, si = packed & 0xFFF;
                if (oi == e.index) continue;
                var o = map.edges[oi];
                Vector2 a = o.pts[si], d = o.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                if (L2 < 1e-6f) continue;
                float t = Vector2.Dot(p - a, d) / L2;
                var tO = d / Mathf.Sqrt(L2);
                if (t <= 0f || t >= 1f)
                {
                    // Beside a segment, not off its end — except past the
                    // OUTSIDE of a bend, where a point is off the end of both
                    // segments that meet at the vertex and nearest the vertex
                    // itself. Every such section lost its squeeze: I-77's
                    // e1877 drew full width 1.5 m into e1891's lanes for two
                    // metres beside e1891's vertex at s=132.7, its rail with
                    // it (WP-04 review). The vertex counts where the road runs
                    // on through it (inside the edge, or a node of two), and
                    // only as far off the end as a bend explains.
                    int vi = t <= 0f ? si : si + 1;
                    bool through = (vi > 0 && vi < o.pts.Length - 1)
                                   || map.nodeEdges[vi == 0 ? o.a : o.b].Count == 2;
                    float over = (t <= 0f ? -t : t - 1f) * Mathf.Sqrt(L2);
                    float perp = Mathf.Abs(Vector2.Dot(p - a, new Vector2(-tO.y, tO.x)));
                    if (!through || over > OutsideBendOverM * perp) continue;
                    // At one height two ribbons drawn into each other are one
                    // pavement (ArmsApart's rule), and a squeeze found at one
                    // section alone notched Ashby Street's edge a metre in
                    // beside Duls Lane's bend, grass in its lane — unless a
                    // median barrier stands on the edge between them, in the
                    // other's lanes wherever the squeeze dropped out (I-77's
                    // carriageways e1255 and e1874, e2739 and e2741).
                    if (Mathf.Abs(o.YAt(o.s[vi]) - y) <= ArmSplitDyM && !Barriered(e) && !Barriered(o)) continue;
                    t = t <= 0f ? 0f : 1f;
                }
                if (Mathf.Abs(Vector2.Dot(tan, tO)) < 0.9f) continue;
                float at = o.s[si] + Mathf.Sqrt(L2) * t;
                // the other ribbon's own centre (the line model offsets it off
                // its OSM line), p being ours
                var q = a + d * t + new Vector2(-tO.y, tO.x) * trims.CentreAt(o, at);
                if ((o.a == e.a || o.a == e.b || o.b == e.a || o.b == e.b) && !ArmsApart(map, trims, e, p, o, at, y)) continue;
                // A different level has nothing to share only where a car
                // fits between them. At a metre it did not: I-277's decks
                // e1910 and e1921 run side by side 1.1 m apart, drew full
                // width over each other, the upper one's rail stood over the
                // lower one's outside lane, and where the split switched off
                // between two sections its own rail stood in its own.
                if (Mathf.Abs(o.YAt(at) - y) > RoadsideRules.CarBandM + CityElevation.DeckThick) continue;
                float dist = Vector2.Distance(p, q);
                if (dist >= hw + trims.HalfWidthAt(o, at) + 0.3f) continue;
                // The road this one carries on from (or into) is never a
                // parallel neighbour. Two arms of one node were already one
                // pavement at one height; a carriageway OSM split twice in a
                // few metres (a bridge's end, a lane drop) reaches the next
                // piece through a connector of a metre or two, and its end
                // read as a road beside us: I-277's e1393 squeezed 6.4 m off
                // the deck e14177 it carries on from through the 1.4 m e2344
                // (Uptown Loop 2,765 m), the union median starting in its
                // lanes (owner 2026-10-04).
                if (Mathf.Abs(o.YAt(at) - y) <= ArmSplitDyM && CarriesOnThrough(map, e, o))
                {
                    carryOnSkipped.Add(e.index);
                    if (!SqueezeCarryOn) continue;
                }
                int side = Vector2.Dot(q - p, right) >= 0f ? 1 : -1;
                if (side > 0) { if (dist < dR) { dR = dist; eR = oi; atR = at; } }
                else { if (dist < dL) { dL = dist; eL = oi; atL = at; } }
            }
            for (int side = -1; side <= 1; side += 2)
            {
                int oi = side > 0 ? eR : eL;
                if (oi < 0) continue;
                // a host or branch of ours on this side: the clip owns it
                if (clipPairs.Contains(PairKey(e.index, oi)) || zonePairs.Contains(PairKey(e.index, oi))) continue;
                var o = map.edges[oi];
                float at = side > 0 ? atR : atL, dist = side > 0 ? dR : dL;
                float hwO = trims.HalfWidthAt(o, at);
                float mine = Mathf.Max(1.2f, dist * hw / (hw + hwO) - 0.15f);
                float theirs = Mathf.Max(1.2f, dist * hwO / (hw + hwO) - 0.15f);
                if (side > 0) { if (mine < hwR) hwR = mine; } else { if (mine < hwL) hwL = mine; }
                if (dist - mine - theirs < 1.2f)
                {
                    float strip = Mathf.Max(0f, dist - Mathf.Min(mine, side > 0 ? hwR : hwL) - Mathf.Min(theirs, hwO));
                    if (side > 0) { nbR = oi; nbAtR = at; stripR = strip; } else { nbL = oi; nbAtL = at; stripL = strip; }
                }
            }
        }

        /// <summary>For the audit: the clip state of an edge at an arc
        /// position, from the LAST tile built. Empty when the edge is not a
        /// clipped branch there.</summary>
        /// <summary>A node as the tile builder draws it, for the launch probe:
        /// its kind and height, every arm's trim and height there (and its
        /// branch host), and a fan's ring of corners (an arm with none is
        /// buried or collapsed: its ribbon starts on the fan).</summary>
        public static string DescribeNode(CityMap map, Trims trims, int n)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"node {n} y {map.nodeY[n]:0.00} {(trims.patch[n] ? "FAN" : trims.mitre[n] ? "MITRE" : "plain")} arms:");
            foreach (int ei in map.nodeEdges[n])
            {
                var e = map.edges[ei];
                if (e.a == e.b) continue;
                float trim = trims.TrimAt(e, n);
                float at = e.a == n ? trim : e.length - trim;
                int host = trims.BranchAt(e, n);
                sb.Append($" e{ei}{(e.link ? "L" : "")} '{e.name}' {(e.a == n ? "a" : "b")} trim {trim:0.0} y@trim {e.YAt(Mathf.Clamp(at, 0f, e.length)):0.00} end {(e.a == n ? e.stY[0] : e.stY[e.stY.Length - 1]):0.00}{(host >= 0 ? " branch-of e" + host : "")}{(ArmCollapsedAtTrim(map, trims, e, n) ? " COLLAPSED" : "")};");
            }
            if (trims.patch[n])
            {
                var ring = new List<FanCorner>();
                FanCorners(map, trims, n, Vector3.zero, ring);
                sb.Append(" corners:");
                foreach (var k in ring) sb.Append($" e{k.edge}/{k.side}{(k.extra ? "x" : "")}{(k.mouthNext ? "m" : "")} ({k.pos.x:0.0},{k.pos.z:0.0}) y {k.pos.y:0.00};");
            }
            return sb.ToString();
        }

        public static string DescribeClip(CityMap map, Trims trims, CityMap.Edge e, float s)
        {
            var clip = ClipAt(e, s);
            if (clip == null) return "";
            var p = e.PointAt(s);
            clip.host.Project(p, out var H, out float sM, out var q, out var dirM, out bool atEnd);
            var rM = new Vector2(-dirM.y, dirM.x);
            float dy = H.YAt(sM) - e.YAt(s);
            float off = Vector2.Dot(p - q, rM) * clip.side;
            float mHalf = HostHalf(map, trims, H, sM, rM, clip.side);
            var tan = e.TangentAt(s);
            var right = new Vector2(-tan.y, tan.x);
            LineModel.CentreAt(e, s, out float cc, out float hc);
            bool cut = CutAgainstHost(map, trims, e, s, p + right * cc, right, hc, e.YAt(s), clip, out float lam, out bool collapsed);
            return $" [clip {clip.sFrom:0}..{clip.sTo:0} side{clip.side} inner{(clip.innerSide > 0 ? "R" : "L")} host e{H.index} '{H.name}' ({clip.host.edges.Count} pieces, {clip.host.Length:0} m) at sM={sM:0}/{H.length:0}{(atEnd ? " END" : "")} off {off:+0.0;-0.0} m dy {dy:+0.00;-0.00} hostHw {mHalf:0.0} cut {(cut ? lam.ToString("+0.0;-0.0") : "none")}{(collapsed ? " COLLAPSED" : "")}{(Mathf.Abs(dy) > AttachDy ? " DETACHED(dy)" : "")}]";
        }

        /// <summary>Every cross-section of one ribbon, into <see cref="sections"/>.</summary>
        static void BuildSections(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e,
                                  float sMin, float sMax)
        {
            var raw = RawSectionsOf(map, trims, e, sMin, sMax);
            sections.Clear();
            var o = tm.origin;
            for (int k = 0; k < raw.Count; k++)
            {
                var sec = raw[k];
                if (!sec.elev && !sec.collapsed && !sec.zoneEnd && sec.s > sMin + 1e-3f && sec.s < sMax - 1e-3f)
                    MeetPavement(map, trims, e, ref sec);
                sec.L -= o; sec.R -= o;
                sections.Add(sec);
            }
        }

        /// <summary>
        /// A ribbon's cross-sections in world space as its tables alone decide
        /// them — samples, taper, clip and squeeze, before MeetPavement —
        /// computed once per tile build and read by the tile's own ribbons, by
        /// <see cref="OutlineOf"/> for the verge solve beside them, and by
        /// MeetPavement itself. A ribbon sectioned for the tile and again as an
        /// outline was half of all the sectioning a tile did. Cleared with the
        /// outlines, since BuildGores fills the clip table.
        /// </summary>
        /// <summary>Time spent sectioning edges the cache did not hold (WP-09's
        /// probe: which big steps are first sight of a long neighbour).</summary>
        static long sectionMissTicks;
        static List<Section> RawSectionsOf(CityMap map, Trims trims, CityMap.Edge e, float sMin, float sMax)
        {
            if (rawSectionCache.TryGetValue(e.index, out var hit)) return hit;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var r = RawSectionsOfMiss(map, trims, e, sMin, sMax);
            sectionMissTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            return r;
        }

        static List<Section> RawSectionsOfMiss(CityMap map, Trims trims, CityMap.Edge e, float sMin, float sMax)
        {
            if (rawSectionCache.TryGetValue(e.index, out var list)) return list;
            list = rawSectionPool.Count > 0 ? rawSectionPool.Pop() : new List<Section>(32);
            list.Clear();
            rawSectionCache[e.index] = list;
            // SamplePositions rebuilds the structure ends a caller may be
            // walking (DecideSideFlags, EmitSide)
            rawSaveEnds.Clear(); rawSaveEnds.AddRange(endScratch);
            SamplePositions(map, e, trims, sMin, sMax);
            for (int k = 0; k < sampleS.Count; k++) list.Add(SectionAt(map, trims, e, sampleS[k], true));
            if (EaseSqueezes) EaseSqueeze(map, trims, e, list, sMin, sMax);
            JoinBuriedEnds(map, trims, e, list, sMin, sMax);
            endScratch.Clear(); endScratch.AddRange(rawSaveEnds);
            return list;
        }

        /// <summary>One cross-section at s: the line model's extents, the clip,
        /// and (when asked) the squeeze.</summary>
        static Section SectionAt(CityMap map, Trims trims, CityMap.Edge e, float s, bool squeeze)
        {
            var tm = worldOrigin;
            {
                var p = e.PointAt(s);
                var right = RightAt(map, trims, e, s, out float widen);
                // (-tan.y, tan.x) is tan turned 90 degrees COUNTER-clockwise
                // in map view, which is the LEFT of travel. So the vertex
                // called L below (p - right*hw) sits on the RIGHT of
                // travel and R on the left; the ribbon's winding is
                // verified with the names as they are, so the names stay
                // and the texture U is mirrored to match: u = 0, the
                // painter's left shoulder, goes on R.
                float y = e.YAt(s);
                if (s <= 1e-3f && trims.mitre[e.a]) y = map.nodeY[e.a];
                else if (s >= e.length - 1e-3f && trims.mitre[e.b]) y = map.nodeY[e.b];
                // THE LINE MODEL: the ribbon spans p - right * eMinus to
                // p + right * ePlus, off-centre where a lane was added on one
                // side; clip and squeeze work about its own centre pc
                LineModel.Extents(e, s, out float eMinus, out float ePlus);
                float hw = 0.5f * (eMinus + ePlus) * widen, cOff = 0.5f * (ePlus - eMinus);
                var pc = p + right * cOff;
                var sec = new Section
                {
                    s = s, right = right, hw = hw, elev = e.ElevatedAt(s), nbL = -1, nbR = -1, stripL = -1f, stripR = -1f,
                    P = p, centre = cOff,
                    L = new Vector3(pc.x - right.x * hw, y, pc.y - right.y * hw),
                    R = new Vector3(pc.x + right.x * hw, y, pc.y + right.y * hw),
                };
                var clip = ClipAt(e, s);
                if (clip != null) ClipSection(map, trims, tm, e, s, pc, right, hw, y, clip, ref sec);
                if (squeeze) SqueezeSection(map, trims, tm, e, pc, right, hw, y, ref sec);
                // roads pass L5: a ramp between its nose and its node
                if (zoneCuts.Count > 0) ApplyZoneCut(map, e, s, ref sec);
                // U at the two edges for a span drawn as ONE quad (both
                // sections full width, EmitRibbon): 1 at the painter's right
                // (the L vertex), 0 at its left. Anything narrower is drawn in
                // paint columns at fixed U, never a squeezed or cropped texture.
                sec.uL = 1f; sec.uR = 0f;
                return sec;
            }
        }

        /// <summary>THE SQUEEZE, EASED (plan A2 I7): the cut each side is never
        /// worked out section by section. The cut the squeeze needs is sampled
        /// on a fixed lattice along the edge (every F / 6 from its a end, the
        /// same samples whichever tile asks), max-filtered and eased in and out
        /// like a taper over the class floor F (street 15, arterial 30,
        /// freeway 90 m; smoothstep), with sections added through every ramp,
        /// so a squeezed edge (and its edge line, which moves with it) runs
        /// smooth instead of following every metre of a neighbour's mapped
        /// line. Never less cut than a section needs. LaneExtents reads the
        /// same envelope, so the audits probe the edge the tile draws.</summary>
        public static bool EaseSqueezes = true;
        static readonly List<float> sqExtra = new List<float>(64);
        static readonly Dictionary<int, float[]> sqLatL = new Dictionary<int, float[]>(), sqLatR = new Dictionary<int, float[]>();
        static float SqueezeFloor(CityMap.Edge e) =>
            (e.cls >= 5 || (e.cls == 4 && e.oneway && !e.link)) ? 90f : (e.link || e.cls >= 2) ? 30f : 15f;

        /// <summary>The raw cut each side at the edge's lattice points (null
        /// when the squeeze cuts nowhere on it).</summary>
        static void SqueezeLattice(CityMap map, Trims trims, CityMap.Edge e, out float[] cl, out float[] cr)
        {
            if (sqLatL.TryGetValue(e.index, out cl)) { cr = sqLatR[e.index]; return; }
            float step = SqueezeFloor(e) / 6f;
            int n = Mathf.Max(2, Mathf.CeilToInt(e.length / step) + 1);
            cl = new float[n]; cr = new float[n];
            bool any = false;
            for (int j = 0; j < n; j++)
            {
                float sj = Mathf.Min(j * step, e.length);
                var p = e.PointAt(sj);
                var t = e.TangentAt(sj);
                var right = new Vector2(-t.y, t.x);
                LineModel.CentreAt(e, sj, out float c, out float hw);
                float hl = hw, hr = hw;
                Squeeze(map, trims, e, p + right * c, right, hw, e.YAt(sj), ref hl, ref hr, out _, out _, out _, out _, out _, out _);
                cl[j] = Mathf.Max(0f, hw - hl); cr[j] = Mathf.Max(0f, hw - hr);
                if (cl[j] > 1e-3f || cr[j] > 1e-3f) any = true;
            }
            if (!any) { cl = null; cr = null; }
            sqLatL[e.index] = cl; sqLatR[e.index] = cr;
        }

        /// <summary>The eased cut on a side (-1 L, +1 R) at s: 0 where the
        /// squeeze cuts nowhere within F.</summary>
        static float EasedCut(CityMap map, Trims trims, CityMap.Edge e, float s, int side)
        {
            SqueezeLattice(map, trims, e, out var cl, out var cr);
            var cut = side < 0 ? cl : cr;
            if (cut == null) return 0f;
            float F = SqueezeFloor(e), step = F / 6f, C = 0f;
            int j0 = Mathf.Max(0, Mathf.FloorToInt((s - F) / step)), j1 = Mathf.Min(cut.Length - 1, Mathf.CeilToInt((s + F) / step));
            for (int j = j0; j <= j1; j++)
            {
                if (cut[j] <= 1e-3f) continue;
                float d = Mathf.Abs(s - Mathf.Min(j * step, e.length));
                if (d >= F) continue;
                C = Mathf.Max(C, cut[j] * (1f - LineModel.Smooth(d / F)));
            }
            return C;
        }

        static void EaseSqueeze(CityMap map, Trims trims, CityMap.Edge e, List<Section> list, float sMin, float sMax)
        {
            // most ribbons are squeezed nowhere: only those whose sections were
            // pay for the lattice
            bool any = false;
            foreach (var c in list) if (c.sqL || c.sqR) { any = true; break; }
            if (!any) return;
            SqueezeLattice(map, trims, e, out var cl, out var cr);
            if (cl == null) return;
            float F = SqueezeFloor(e), step = F / 6f;
            // sections through the ramps: at every lattice point within F of a cut
            sqExtra.Clear();
            for (int j = 0; j < cl.Length; j++)
            {
                if (cl[j] <= 1e-3f && cr[j] <= 1e-3f) continue;
                for (int k = -6; k <= 6; k++)
                {
                    int q = j + k;
                    if (q < 0 || q >= cl.Length) continue;
                    float sv = Mathf.Min(q * step, e.length);
                    if (sv > sMin + 0.3f && sv < sMax - 0.3f) sqExtra.Add(sv);
                }
            }
            if (sqExtra.Count > 0)
            {
                sqExtra.Sort();
                int j = 0;
                var merged = new List<Section>(list.Count + sqExtra.Count);
                float lastS = float.NegativeInfinity;
                for (int i = 0; i < list.Count; i++)
                {
                    while (j < sqExtra.Count && sqExtra[j] < list[i].s)
                    {
                        float sv = sqExtra[j++];
                        // not within a fifth of a step of a section already there
                        if (sv - lastS < step * 0.2f || list[i].s - sv < step * 0.2f) continue;
                        merged.Add(SectionAt(map, trims, e, sv, true)); lastS = sv;
                    }
                    merged.Add(list[i]); lastS = list[i].s;
                }
                list.Clear(); list.AddRange(merged);
            }
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c.collapsed) continue;
                var pc = c.P + c.right * c.centre;
                for (int side = -1; side <= 1; side += 2)
                {
                    // a clipped inner side stays on its host's edge
                    if (c.clippedIn && c.innerSide == side) continue;
                    float C = EasedCut(map, trims, e, c.s, side);
                    if (C <= 1e-3f) continue;
                    var v = side < 0 ? c.L : c.R;
                    float cur = Vector2.Distance(new Vector2(v.x, v.z), pc);
                    float ext = c.hw - C;
                    if (ext >= cur - 1e-3f) continue;   // the section's own squeeze already cuts this far
                    var q = pc + c.right * (side * ext);
                    v = new Vector3(q.x, v.y, q.y);
                    if (side < 0) { c.L = v; c.sqL = true; } else { c.R = v; c.sqR = true; }
                }
                list[i] = c;
            }
        }

        /// <summary>A join eases back to the ribbon's own solve over this much
        /// run per metre of height it has to make up (a 3% grade), and never
        /// over less than <see cref="JoinMinM"/>.</summary>
        const float JoinGrade = 0.03f, JoinMinM = 4f;

        /// <summary>
        /// A BURIED ARM STARTS ON THE FAN, AT THE FAN'S HEIGHT. An arm whose
        /// whole mouth lies under the fan its two neighbours make is dropped
        /// from the fan's ring (<see cref="FanCorners"/>) and its ribbon starts
        /// ON that fan - but at its own height, while the fan there is made of
        /// the neighbours' corners and the node. Where the two disagree the
        /// ribbon's first section stood up out of the fan as a kerb across
        /// the lanes: East Woodlawn Road's e11126 in the South Boulevard fan,
        /// 0.26 m over it, stopped a car dead at 110 km/h (the owner, 2026-09-30:
        /// "All roads should meet at smooth junctions and transitions"). So the
        /// ribbon's first section takes the fan's own height under each of its
        /// two vertices, and the ribbon eases back to its solve from there
        /// (<see cref="JoinGrade"/>), never past its other end, which keeps its
        /// own joint; both ends buried, each eases over half. World space.
        /// </summary>
        static void JoinBuriedEnds(CityMap map, Trims trims, CityMap.Edge e, List<Section> list, float sMin, float sMax)
        {
            int n = list.Count;
            if (n < 2 || e.a == e.b) return;
            float dLa = 0f, dRa = 0f, dLb = 0f, dRb = 0f;
            bool atA = BuriedEndOffset(map, trims, e, e.a, list[0], out dLa, out dRa);
            bool atB = BuriedEndOffset(map, trims, e, e.b, list[n - 1], out dLb, out dRb);
            if (!atA && !atB) return;
            float span = sMax - sMin, share = atA && atB ? 0.5f : 1f;
            for (int end = 0; end < 2; end++)
            {
                if (end == 0 ? !atA : !atB) continue;
                float dL = end == 0 ? dLa : dLb, dR = end == 0 ? dRa : dRb;
                float reach = Mathf.Min(Mathf.Max(Mathf.Max(Mathf.Abs(dL), Mathf.Abs(dR)) / JoinGrade, JoinMinM), span * share);
                for (int k = 0; k < n; k++)
                {
                    float dist = end == 0 ? list[k].s - sMin : sMax - list[k].s;
                    float w = reach > 1e-3f ? 1f - dist / reach : (dist <= 1e-3f ? 1f : 0f);
                    if (w <= 0f) continue;
                    var sec = list[k];
                    sec.L.y += dL * w; sec.R.y += dR * w;
                    list[k] = sec;
                }
            }
        }

        /// <summary>How far this end section's two vertices stand off the fan
        /// they start on, when the arm is buried in the fan at that node
        /// (below it: positive). False where the arm is not buried, or the
        /// offsets are under a centimetre.</summary>
        static bool BuriedEndOffset(CityMap map, Trims trims, CityMap.Edge e, int node, Section sec, out float dL, out float dR)
        {
            dL = dR = 0f;
            // (FanCorners buries only among four arms or more)
            if (!trims.patch[node] || map.nodeEdges[node].Count < 4 || ArmCollapsedAtTrim(map, trims, e, node)) return false;
            var (ring, tris) = FanRing(map, trims, node);
            if (ring.Count < 3 || tris.Count < 3) return false;
            foreach (var k in ring) if (k.edge == e.index) return false;   // it has a mouth: not buried
            if (!FanY(map, trims, node, ring, tris, new Vector2(sec.L.x, sec.L.z), out float yL) ||
                !FanY(map, trims, node, ring, tris, new Vector2(sec.R.x, sec.R.z), out float yR)) return false;
            dL = yL - FanProudM - sec.L.y;
            dR = yR - FanProudM - sec.R.y;
            return Mathf.Abs(dL) >= 0.01f || Mathf.Abs(dR) >= 0.01f;
        }

        /// <summary>Each patch node's ring and triangles (world space), per
        /// tile build: <see cref="FanCorners"/> reads the tile's clip table.</summary>
        static readonly Dictionary<int, (List<FanCorner> ring, List<int> tris)> fanRings = new Dictionary<int, (List<FanCorner>, List<int>)>();
        static readonly Dictionary<int, List<Vector3>> fanRingSteiner = new Dictionary<int, List<Vector3>>();
        static (List<FanCorner> ring, List<int> tris) FanRing(CityMap map, Trims trims, int node)
        {
            if (fanRings.TryGetValue(node, out var r)) return r;
            var ring = new List<FanCorner>(12);
            var tris = new List<int>(36);
            var st = new List<Vector3>(4);
            FanCorners(map, trims, node, Vector3.zero, ring);
            FanCentre(map, trims, node, out var fc, out _);
            if (ring.Count >= 3)
            {
                FanTriangles(ring, fc, tris);
                FanSteiner(map, trims, node, Vector3.zero, st);
                RefineFan(ring, fc, st, tris);
            }
            r = (ring, tris);
            fanRings[node] = r;
            fanRingSteiner[node] = st;
            return r;
        }

        /// <summary>The fan's surface over a plan point: the triangle that holds
        /// it (or, a hand's width outside them all, the nearest, carried on
        /// flat), interpolated.</summary>
        static bool FanY(CityMap map, Trims trims, int node, List<FanCorner> ring, List<int> tris, Vector2 q, out float y)
        {
            y = 0f;
            FanCentre(map, trims, node, out var np, out float cy);
            cy += FanProudM;
            fanRingSteiner.TryGetValue(node, out var st);
            float best = float.NegativeInfinity;
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                Vector3 P(int i) => FanVertex(i, new Vector3(np.x, cy, np.y), ring, st);
                Vector3 a = P(tris[t]), b = P(tris[t + 1]), c = P(tris[t + 2]);
                float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(d) < 1e-6f) continue;
                float w1 = ((b.z - c.z) * (q.x - c.x) + (c.x - b.x) * (q.y - c.z)) / d;
                float w2 = ((c.z - a.z) * (q.x - c.x) + (a.x - c.x) * (q.y - c.z)) / d;
                float w3 = 1f - w1 - w2;
                float worst = Mathf.Min(w1, Mathf.Min(w2, w3));
                if (worst <= best) continue;
                best = worst;
                float c1 = Mathf.Max(w1, 0f), c2 = Mathf.Max(w2, 0f), c3 = Mathf.Max(w3, 0f), sum = c1 + c2 + c3;
                y = sum > 1e-6f ? (c1 * a.y + c2 * b.y + c3 * c.y) / sum : a.y;
            }
            return best > -0.25f;   // inside one, or within a quarter of a triangle of it
        }
        static readonly Dictionary<int, List<Section>> rawSectionCache = new Dictionary<int, List<Section>>();
        static readonly Stack<List<Section>> rawSectionPool = new Stack<List<Section>>();
        static readonly List<float> rawSaveEnds = new List<float>(8);
        static void ClearSectionCaches()
        {
            foreach (var kv in rawSectionCache) rawSectionPool.Push(kv.Value);
            rawSectionCache.Clear();
            sqLatL.Clear(); sqLatR.Clear();
            rawOutlines.Clear();
            fanRings.Clear();
            fanRingSteiner.Clear();
        }

        /// <summary>
        /// TWO PAVEMENTS THAT TOUCH MEET. A section's edge vertex standing more
        /// than RoadsideRules.EdgeDropM over another grounded road's pavement
        /// as drawn within <see cref="MeetReachM"/> past it — under it, or a
        /// verge's pad beyond — steps down onto that pavement's height, by at
        /// most <see cref="MeetDyM"/>. Each road is drawn from its own cross-
        /// sections and its own solve, so where two touch the seam between
        /// them stood 5-6 cm high, past the edge drop a wheel may meet, with a
        /// strip an inch under the lower one between them: a branch's edge
        /// over its gore past the end of its cut (e339 beside West Morehead
        /// Street), and a link running along North Graham Street's edge 5 cm
        /// off it at a junction (e14090 beside e15420). The DOT answer is that
        /// the two meet within the edge drop; the UPPER one comes down, so a
        /// pair never moves toward each other, and its cross-section tilts by
        /// no more than MeetDyM across its width. The other pavement is read as
        /// sectioned WITHOUT this step (<see cref="RawOutlineOf"/>), so which
        /// of two roads is sectioned first changes neither. Not at a trim,
        /// where the edge meets a fan or a mitred node, nor on structure.
        /// World space.
        /// </summary>
        static void MeetPavement(CityMap map, Trims trims, CityMap.Edge e, ref Section sec)
        {
            var p = e.PointAt(sec.s);
            float hw = sec.hw;
            // The roads it can touch without being drawn into it: the other
            // arms of its two junctions, running off beside it, and the host
            // chain it is clipped against. Two roads crossing over each other
            // are the squeeze's and the clip's, and every road city-wide
            // gathered per section cost a tile build a tenth.
            meetEdges.Clear();
            var vL = new Vector2(sec.L.x, sec.L.z);
            var vR = new Vector2(sec.R.x, sec.R.z);
            foreach (var (oi, node) in MeetArmsOf(map, e))
                MeetCandidate(map, trims, e, oi, node, p, hw, vL, vR, sec.L.y, sec.R.y);
            var clip = ClipAt(e, sec.s);
            if (clip != null)
                foreach (var H in clip.host.edges) MeetCandidate(map, trims, e, H.index, -1, p, hw, vL, vR, sec.L.y, sec.R.y);
            bool gored = goreEdges.Contains(e.index);
            if (meetEdges.Count == 0 && !gored) return;
            for (int side = -1; side <= 1; side += 2)
            {
                var v = sec.Edge(side);
                var vw = new Vector2(v.x, v.z);
                var outw = sec.Out(side);
                bool flush = false;
                float best = float.NegativeInfinity;
                for (int m = 0; m < meetEdges.Count && !flush; m++)
                {
                    var ol = RawOutlineOf(map, trims, meetEdges[m]);
                    if (ol.L == null || vw.x < ol.minX - MeetReachM || vw.x > ol.maxX + MeetReachM || vw.y < ol.minZ - MeetReachM || vw.y > ol.maxZ + MeetReachM) continue;
                    for (int i = 1; i < ol.L.Length && !flush; i++)
                    {
                        if (ol.BlockMisses(i, vw.x - MeetReachM, vw.y - MeetReachM, vw.x + MeetReachM, vw.y + MeetReachM)) { i += Outline.Block - 1; continue; }
                        if (ol.deck[i]) continue;
                        Vector3 aL = ol.L[i - 1], bL = ol.L[i], bR = ol.R[i], aR = ol.R[i - 1];
                        if (vw.x < Mathf.Min(Mathf.Min(aL.x, bL.x), Mathf.Min(bR.x, aR.x)) - MeetReachM || vw.x > Mathf.Max(Mathf.Max(aL.x, bL.x), Mathf.Max(bR.x, aR.x)) + MeetReachM ||
                            vw.y < Mathf.Min(Mathf.Min(aL.z, bL.z), Mathf.Min(bR.z, aR.z)) - MeetReachM || vw.y > Mathf.Max(Mathf.Max(aL.z, bL.z), Mathf.Max(bR.z, aR.z)) + MeetReachM) continue;
                        for (int tri = 0; tri < 2; tri++)
                        {
                            Vector3 A = aL, B = tri == 0 ? bL : bR, C = tri == 0 ? bR : aR;
                            if (!TriInterval(A, B, C, vw, outw, MeetReachM, out float t0, out _)) continue;
                            float h = TriHeight(A, B, C, vw + outw * t0);
                            if (h > v.y + MeetDyM) continue;                 // not a pavement this edge meets
                            if (h >= v.y - RoadsideRules.EdgeDropM) { flush = true; break; }
                            if (h >= v.y - MeetDyM) best = Mathf.Max(best, h);
                        }
                    }
                }
                // and the painted gores, which lie an inch under the two roads
                // either side: a branch's edge past the end of its cut stood
                // over its gore at its own height, and the gore's nose verge
                // beside it, laid at the host's, 5.7 cm down (e339)
                for (int gg = 0; gored && gg < goreGroups.Count && !flush; gg++)
                {
                    var gbox = goreGroups[gg].box;
                    if (vw.x < gbox.x - MeetReachM || vw.x > gbox.z + MeetReachM || vw.y < gbox.y - MeetReachM || vw.y > gbox.w + MeetReachM) continue;
                    for (int g = goreGroups[gg].from; g < goreGroups[gg].to && !flush; g++)
                    {
                        var (ga, gb, gc, gd) = goreQuads[g];
                        if (vw.x < Mathf.Min(Mathf.Min(ga.x, gb.x), Mathf.Min(gc.x, gd.x)) - MeetReachM || vw.x > Mathf.Max(Mathf.Max(ga.x, gb.x), Mathf.Max(gc.x, gd.x)) + MeetReachM ||
                            vw.y < Mathf.Min(Mathf.Min(ga.z, gb.z), Mathf.Min(gc.z, gd.z)) - MeetReachM || vw.y > Mathf.Max(Mathf.Max(ga.z, gb.z), Mathf.Max(gc.z, gd.z)) + MeetReachM) continue;
                        for (int tri = 0; tri < 2; tri++)
                        {
                            Vector3 A = ga, B = tri == 0 ? gb : gc, C = tri == 0 ? gc : gd;
                            if (!TriInterval(A, B, C, vw, outw, MeetReachM, out float t0, out _)) continue;
                            float h = TriHeight(A, B, C, vw + outw * t0) + RoadsideRules.EdgeDropM;
                            if (h > v.y + MeetDyM) continue;
                            if (h >= v.y - RoadsideRules.EdgeDropM) { flush = true; break; }
                            if (h >= v.y - MeetDyM) best = Mathf.Max(best, h);
                        }
                    }
                }
                if (flush || best == float.NegativeInfinity) continue;
                v.y = best;
                if (side < 0) sec.L = v; else sec.R = v;
            }
        }

        /// <summary>Add a road to MeetPavement's candidates if one of its
        /// segments passes near enough to either edge vertex to touch it:
        /// near that road's EDGE (a vertex deep in its lanes is two roads
        /// drawn into each other), no more than a step away in height, and,
        /// for the next arm of a junction, off the mouth the fan paves.</summary>
        static void MeetCandidate(CityMap map, Trims trims, CityMap.Edge e, int oi, int node, Vector2 p, float hw,
                                  Vector2 vL, Vector2 vR, float yL, float yR)
        {
            if (oi == e.index || meetEdges.Contains(oi)) return;
            var o = map.edges[oi];
            if (node >= 0)
            {
                float near = hw + o.HalfMax + MeetNodeM;
                if ((map.nodes[node] - p).sqrMagnitude < near * near) return;
            }
            for (int si = 0; si + 1 < o.pts.Length; si++)
            {
                Vector2 a = o.pts[si], d = o.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                if (L2 < 1e-6f) continue;
                float tL = Mathf.Clamp01(Vector2.Dot(vL - a, d) / L2), tR = Mathf.Clamp01(Vector2.Dot(vR - a, d) / L2);
                float at = o.s[si] + Mathf.Sqrt(L2) * 0.5f * (tL + tR);
                // its edge on the side each vertex is on (the line model)
                var rO = new Vector2(-d.y, d.x);
                float hwOL = ExtToward(o, at, rO, Vector2.Dot(vL - a, rO) >= 0f ? 1f : -1f);
                float hwOR = ExtToward(o, at, rO, Vector2.Dot(vR - a, rO) >= 0f ? 1f : -1f);
                if (Mathf.Abs(Vector2.Distance(vL, a + d * tL) - hwOL) > MeetReachM + MeetSlackM &&
                    Mathf.Abs(Vector2.Distance(vR, a + d * tR) - hwOR) > MeetReachM + MeetSlackM) continue;
                float yO = o.YAt(at);
                if (Mathf.Abs(yO - yL) > MeetDyM + MeetSlackM && Mathf.Abs(yO - yR) > MeetDyM + MeetSlackM) continue;
                meetEdges.Add(oi);
                return;
            }
        }

        /// <summary>The other arms of an edge's two junctions that run beside
        /// it close enough to touch somewhere past the mouth the fan paves,
        /// within <see cref="MeetArmWalkM"/> of the node. A fact of the map,
        /// so each edge is walked once per map, not once per section.</summary>
        static List<(int oi, int node)> MeetArmsOf(CityMap map, CityMap.Edge e)
        {
            if (meetMap != map) { meetMap = map; meetArms = new List<(int, int)>[map.edges.Length]; }
            var list = meetArms[e.index];
            if (list != null) return list;
            list = new List<(int, int)>(0);
            meetArms[e.index] = list;
            for (int end = 0; end < 2; end++)
            {
                int n = end == 0 ? e.a : e.b;
                foreach (int oi in map.nodeEdges[n])
                {
                    if (oi == e.index || list.Contains((oi, n))) continue;
                    var o = map.edges[oi];
                    float lim = (e.width + o.width) * 0.5f + MeetReachM + MeetSlackM;
                    float zone = (e.width + o.width) * 0.5f + MeetNodeM;
                    for (float w = zone; w <= Mathf.Min(o.length, MeetArmWalkM); w += MeetArmStepM)
                    {
                        var q = o.PointAt(o.a == n ? w : o.length - w);
                        if ((q - map.nodes[n]).sqrMagnitude < zone * zone) continue;
                        CityElevation.ProjectOn(e, q, out float se);
                        if ((q - e.PointAt(se)).sqrMagnitude > lim * lim) continue;
                        list.Add((oi, n));
                        break;
                    }
                }
            }
            return list;
        }
        static CityMap meetMap;
        static List<(int oi, int node)>[] meetArms;
        static readonly List<int> meetEdges = new List<int>(16);
        /// <summary>How far from a junction MeetArmsOf walks an arm, and its
        /// step: two roads still within a hand of touching this far out
        /// leave the node within a few degrees of each other.</summary>
        const float MeetArmWalkM = 60f, MeetArmStepM = 1f;
        /// <summary>How far past an edge vertex MeetPavement looks for the
        /// pavement beside it: a verge's clearance pad, the least room a verge
        /// or connector is ever given between two roads.</summary>
        const float MeetReachM = VergeClearPadM;
        /// <summary>The most an edge vertex steps down to meet the pavement
        /// beside it: the drive audit's limit on a lane step in half a metre.
        /// Two roads further apart in height are the squeeze's and the
        /// clip's.</summary>
        const float MeetDyM = 0.12f;
        /// <summary>Slack on the distances a neighbour is gathered by: a
        /// mitred section is wider than its half width.</summary>
        const float MeetSlackM = 0.5f;
        /// <summary>How far past two arms' combined half widths from their
        /// shared node MeetPavement leaves their edges to the fan between.</summary>
        const float MeetNodeM = 4f;
        /// <summary>Every edge of a host or branch chain BuildGores walked: the
        /// only ribbons a painted gore lies beside.</summary>
        static readonly HashSet<int> goreEdges = new HashSet<int>();

        /// <summary>
        /// For the roadside audit: what the LAST tile built decided for the
        /// span of an edge's side at an arc position — deck, wedge, approach;
        /// gap, rail, retaining face, cut wall, median barrier, squeeze strip —
        /// so a failing probe point names the rule that drew what it met.
        /// Rebuilds that edge's sections, so call it only for points that
        /// fail.
        /// </summary>
        public static string DescribeSide(CityMap map, Trims trims, CityMap.Edge e, float s, int side)
        {
            var tm = new TileMeshes { origin = tileOrigin };
            var min = new Vector2(tileOrigin.x, tileOrigin.z);
            float sMin = trims.atA[e.index], sMax = e.length - trims.atB[e.index];
            if (sMax - sMin < 0.6f) return "";
            BuildSections(map, trims, tm, e, sMin, sMax);
            if (sections.Count < 2) return "";
            DecideSideFlags(map, trims, tm, e, min, min + Vector2.one * TileSize);
            for (int i = 1; i < sections.Count; i++)
            {
                var A = sections[i - 1]; var B = sections[i];
                if (s < A.s - 1e-3f || s > B.s + 1e-3f) continue;
                var f = spanFlags[i]; var sf = f[side];
                var sb = new System.Text.StringBuilder($" | span {A.s:0.0}..{B.s:0.0}:");
                if (f.elev) sb.Append(GroundedDeckEnd(map, trims, tm, e, i, side) ? " deck(grounded end, verge)" : " deck");
                if (f.wedge) sb.Append(" wedge");
                if (f.skip) sb.Append(" skip");
                if (f.approach) sb.Append(" approach");
                if (sf.gap) sb.Append(" gap");
                if (sf.rail) sb.Append(" rail");
                if (sf.retain) sb.Append(" retaining-face");
                if (sf.cut) sb.Append(" cut-wall");
                if (sf.median) sb.Append(" median");
                if (sf.union)
                {
                    var ur = UnionOn(trims, e.index, side, A.s, B.s);
                    if (ur != null) sb.Append($" union({(ur.owner ? "owner" : "partner")} of e{ur.nb} {ur.median}{(ur.approach ? " approach" : "")} {ur.s0:0.0}..{ur.s1:0.0})");
                }
                float strip = Mathf.Max(A.Strip(side), B.Strip(side));
                if (strip >= 0f) sb.Append($" squeezed(strip {strip:0.00} nb e{(A.Nb(side) >= 0 ? A.Nb(side) : B.Nb(side))})");
                if (A.clippedIn || B.clippedIn) sb.Append(" clipped");
                if (!f.elev && !sf.rail && !sf.cut && !sf.gap && !sf.union && strip < 0f) sb.Append(" verge");
                return sb.ToString();
            }
            return " | span ?";
        }

        /// <summary>For the audit: every section of an edge as the LAST tile
        /// would draw it — lateral offset of each vertex from the centreline
        /// and the clip / squeeze flags.</summary>
        public static string DescribeSections(CityMap map, Trims trims, CityMap.Edge e)
        {
            var tm = new TileMeshes { origin = Vector3.zero };
            BuildSections(map, trims, tm, e, trims.atA[e.index], e.length - trims.atB[e.index]);
            var sb = new System.Text.StringBuilder();
            sb.Append($"      sections of e{e.index} '{e.name}'{(e.link ? " L" : "")} len {e.length:0} hw {e.width * 0.5f:0.0}");
            if (clips.TryGetValue(e.index, out var clist))
                foreach (var c in clist) sb.Append($" clip {c.sFrom:0}..{c.sTo:0} side{c.side} inner{(c.innerSide > 0 ? "R" : "L")} host e{c.host.edges[0].index}+{c.host.edges.Count - 1}");
            sb.Append(":\n");
            foreach (var sec in sections)
            {
                var p = e.PointAt(sec.s);
                float latL = Vector2.Dot(new Vector2(sec.L.x, sec.L.z) - p, sec.right);
                float latR = Vector2.Dot(new Vector2(sec.R.x, sec.R.z) - p, sec.right);
                sb.Append($"        s={sec.s:0.0} y={sec.L.y:0.00}{(Mathf.Abs(sec.R.y - sec.L.y) > 0.005f ? "/" + sec.R.y.ToString("0.00") : "")} L{latL:+0.0;-0.0} R{latR:+0.0;-0.0}{(sec.elev ? " deck" : "")}{(sec.collapsed ? " COLLAPSED" : sec.clippedIn ? " clippedIn" : "")}{(sec.innerSide != 0 ? " inner" + (sec.innerSide > 0 ? "R" : "L") : "")}{(sec.nbL >= 0 ? " nbL" + sec.nbL : "")}{(sec.nbR >= 0 ? " nbR" + sec.nbR : "")}\n");
            }
            return sb.ToString();
        }

        /// <summary>For the audit: the host edge a branch is clipped against
        /// at an arc position, or -1.</summary>
        public static int HostEdgeAt(CityMap.Edge e, float s)
        {
            var clip = ClipAt(e, s);
            if (clip == null) return -1;
            clip.host.Project(e.PointAt(s), out var H, out _, out _, out _);
            return H.index;
        }

        /// <summary>Clip one branch section against its host chain.</summary>
        static void ClipSection(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, float s,
                                Vector2 p, Vector2 right, float hw, float y, Clip clip, ref Section sec)
        {
            if (!CutAgainstHost(map, trims, e, s, p, right, hw, y, clip, out float lam, out bool collapsed)) return;
            sec.innerSide = clip.innerSide;
            // on the host's edge, at the host's height there: the two
            // surfaces meet along the seam instead of stepping (the branch
            // may sit a few tens of centimetres above or below its host)
            var vp = p + right * lam;
            clip.host.Project(vp, out var Hv, out float sMv, out _, out _);
            float yv = Mathf.Abs(Hv.YAt(sMv) - y) <= AttachDy ? Hv.YAt(sMv) : y;
            var v = new Vector3(vp.x - tm.origin.x, yv, vp.y - tm.origin.z);
            if (collapsed)
            {
                // the whole ribbon is inside the host: a zero-width section on its edge
                sec.L = v; sec.R = v;
                sec.collapsed = true;
                sec.clippedIn = true;
            }
            else
            {
                if (clip.innerSide > 0) sec.R = v; else sec.L = v;
                sec.clippedIn = true;
            }
        }

        /// <summary>
        /// Where the host's edge line cuts this section's own cross-line:
        /// the lateral <paramref name="lam"/> (signed along <paramref name="right"/>)
        /// at which the inner vertex sits. Cutting along the cross-line
        /// rather than sliding the vertex sideways is what makes a road that
        /// meets its host at forty-five degrees end in a straight mouth on
        /// the host's edge instead of a skewed sliver; for a shallow merge
        /// the two are the same. False when nothing needs clipping.
        /// </summary>
        static bool CutAgainstHost(CityMap map, Trims trims, CityMap.Edge e, float s, Vector2 p, Vector2 right,
                                   float hw, float y, Clip clip, out float lam, out bool collapsed)
        {
            lam = 0f; collapsed = false;
            clip.host.Project(p, out var H, out float sM, out var q, out var dirM, out bool atEnd);
            if (atEnd) return false;                                // the host ran out here
            if (Mathf.Abs(H.YAt(sM) - y) > AttachDy) return false;   // climbed away: not beside the host here
            var rM = new Vector2(-dirM.y, dirM.x);
            int side = clip.side;
            float mHalf = HostHalf(map, trims, H, sM, rM, side) + ClipLift;
            float off = Vector2.Dot(p - q, rM) * side;             // our centreline, positive on our side of the host
            float denom = Vector2.Dot(right, rM) * side;           // our cross-line against the host's outward normal
            if (Mathf.Abs(denom) < 0.3f) return false;             // near-perpendicular: no branch geometry here
            lam = (mHalf - off) / denom;                           // the cut, signed along right
            // our inner vertex is at -hw (L) when denom > 0, +hw (R) when denom < 0
            float innerLam = denom > 0f ? -hw : hw;
            float outerLam = -innerLam;
            bool innerInside = denom > 0f ? lam > innerLam : lam < innerLam;
            if (!innerInside) return false;
            bool outerInside = denom > 0f ? lam >= outerLam - 0.15f : lam <= outerLam + 0.15f;
            if (outerInside) { collapsed = true; lam = Mathf.Clamp(lam, -hw - 6f, hw + 6f); }
            return true;
        }

        /// <summary>The natural ground stands this far above the tarmac
        /// beside the outside edge = the road is in a cut, and the outside
        /// edge gets a retaining wall.</summary>
        public const float CutWallM = 2.0f;

        /// <summary>
        /// Does this span's outside edge need a RETAINING WALL? Only where a
        /// graded cut cannot fit (WP-14; the owner's DOT rule: walls only where
        /// a slope cannot). The land beside must first stand well above the
        /// road - <see cref="CutWallM"/> at 4, 8 and 12 m out on the DEM (the
        /// WP-04 guard) - and then the cut Ground grades there
        /// (CityElevation.SectionCut: level across the lattice band, the 1V:3H
        /// back slope, steepened to a 1V:2H bank only where the back slope
        /// cannot meet the land by the section's reach) must not fit: even
        /// the 1V:2H bank would have to start inside the band, or something
        /// stands in the slope before it meets the land - another road's
        /// pavement well above this one (a street along the top of the
        /// trench) or a building. So every unwalled cut is a graded bank no
        /// steeper than 1V:2H (CityLandProbe measures it). Decided from data
        /// alone, so every tile sees the same run. Until WP-14 every span
        /// whose DEM passed the guard was walled: 1.07 km on the audited
        /// tiles, the graded slope behind hidden by a 24 m shelf.
        /// </summary>
        static bool InCut(CityMap map, CityMap.Edge e, TileMeshes tm, Section A, Section B, Vector2 outward, out byte why)
        {
            why = 0;
            var m = (A.L + B.L) * 0.5f;
            var p = new Vector2(m.x + tm.origin.x, m.z + tm.origin.z);
            foreach (float o in CutProbeM)
            {
                var q = p + outward * o;
                if (!(CityElevation.BaseY(q.x, q.y) - m.y > CutWallM)) return false;
            }
            if (CutWallsEverywhere) { why = 1; return true; }
            // THE SECTION (CityElevation.SectionCut, the one Ground grades
            // by): level across the band, the 1V:3H back slope, steepened to
            // the 1V:2H bank only where that cannot meet the land by the
            // reach. A wall only where even the bank cannot fit: it would
            // have to start inside the band, over the road's own level.
            float band = RoadsideRules.CityCutStartM(e.cls, e.link);
            float sMid = (A.s + B.s) * 0.5f;
            float reach = CityElevation.SectionReachM(e.PaveEdgeM(sMid, e.SideAt(sMid, p + outward)));
            var rq = p + outward * reach;
            float atReach = CityElevation.BaseY(rq.x, rq.y);
            float pin = m.y - CityElevation.CorridorSink;
            float land0 = CityElevation.BaseY(p.x + outward.x * band, p.y + outward.y * band);
            if (CityElevation.SectionCut(pin, band, band, reach, land0, atReach) > pin + CutBankTolM) { why = 1; return true; }
            float day = reach;
            for (float o = band; o <= reach + 1e-3f; o += CutDaylightStepM)
            {
                var q = p + outward * o;
                float land = CityElevation.BaseY(q.x, q.y);
                if (land <= CityElevation.SectionCut(pin, band, o, reach, land, atReach) + 1e-3f) { day = o; break; }
            }
            // ...and nothing in the slope before it
            cutNear.Clear();
            var far = p + outward * day;
            map.EdgeSegsInRect(Vector2.Min(p, far) - Vector2.one * CityElevation.MaxCorridorHalf, Vector2.Max(p, far) + Vector2.one * CityElevation.MaxCorridorHalf, cutNear);
            for (float o = 2f; o <= day + 1e-3f; o += CutDaylightStepM)
            {
                var q = p + outward * o;
                if (map.AnyFootprintNear(q, 1f)) { why = 3; return true; }
                foreach (int packed in cutNear)
                {
                    int oi = packed >> 12, si = packed & 0xFFF;
                    if (oi == e.index) continue;
                    var oe = map.edges[oi];
                    Vector2 a = oe.pts[si], d = oe.pts[si + 1] - a;
                    float L2 = d.sqrMagnitude;
                    float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(q - a, d) / L2) : 0f;
                    float oat = oe.s[si] + Mathf.Sqrt(L2) * t;
                    if (Vector2.Distance(q, a + d * t) > oe.PaveEdgeM(oat, oe.SideOf(si, q)) + 1f) continue;
                    // a deck holds no land (CityElevation.Ground): the slope
                    // runs on under it to the abutment (leftover item 1 finish)
                    if (CityElevation.RprfOn && oe.ElevatedAt(oat)) continue;
                    if (oe.YAt(oat) - m.y > CutWallM)
                    {
                        // THE DOT RULE (the owner, 2026-09-13; leftover item 1
                        // finish): a graded slope wherever there is room. The
                        // road above stands in the slope only where Ground cannot
                        // grade between the two: its 1V:4H fill, from its bench,
                        // still stands over this road's back slope where this
                        // road's lattice cap stops (CityLatticeReachM out) - the
                        // drop the cap would leave is what the wall retains.
                        // Further out the two meet on a graded bank (the lidar's
                        // real cuts put streets 20-40 m back along the top: walled
                        // 214 -> 1,035 m on the roadside tiles, mostly there).
                        float lr = RoadsideRules.CityLatticeReachM;
                        float oEdge = o + 1f;
                        float capF = pin + Mathf.Max(0f, lr - RoadsideRules.CityCutBandM) * RoadsideRules.BackSlope;
                        float floorU = oe.YAt(oat) - CityElevation.CorridorSink
                                       - Mathf.Max(0f, oEdge - lr - RoadsideRules.CityBenchM(oe.cls, oe.link)) * RoadsideRules.CityFillSlope;
                        if (CityElevation.RprfOn && oEdge > lr && floorU <= capF + CutBankTolM) continue;
                        why = 2; return true;
                    }
                }
            }
            return false;
        }
        /// <summary>Where the cut test samples the DEM. PSX_CITY_CUTWALL_ONE=1
        /// in the environment puts back the pre-WP-04 single sample, 4 m out,
        /// so the audit can measure the old wall length on the same code (a
        /// measuring switch; nothing sets it in a build).</summary>
        static readonly float[] CutProbeM =
            System.Environment.GetEnvironmentVariable("PSX_CITY_CUTWALL_ONE") == "1" ? new[] { 4f } : new[] { 4f, 8f, 12f };
        /// <summary>PSX_CITY_CUTWALL_ALL=1 walls every span the DEM guard
        /// passes, as before WP-14, so the audit can measure what the graded
        /// cuts saved on the same code (a measuring switch).</summary>
        static readonly bool CutWallsEverywhere =
            System.Environment.GetEnvironmentVariable("PSX_CITY_CUTWALL_ALL") == "1" || System.Environment.GetEnvironmentVariable("PSX_CITY_CUTWALL_ONE") == "1";
        const float CutDaylightStepM = 2f;
        /// <summary>How far over the road's own level the 1V:2H bank may have
        /// to start before the cut is walled instead (float noise and the
        /// DEM's bilinear wobble).</summary>
        const float CutBankTolM = 0.25f;
        static readonly HashSet<int> cutNear = new HashSet<int>();

        static Vector3 Flat(Vector2 v) => new Vector3(v.x, 0f, v.y);

        /// <summary>A Jersey barrier or retaining wall along one edge of a
        /// span: inner face, top and outer face, standing on the pavement
        /// edge and reaching outward, its foot below the verge's first inch so
        /// no slot shows under it; capped where its run ends.</summary>
        static void EmitBarrier(Vector3 a, Vector3 b, Vector2 outward, float v0, float v1, bool capA, bool capB,
                                float hA = BarrierH, float hB = BarrierH)
        {
            var bk = barrierBucket;
            var o = Flat(outward) * BarrierW;
            var upA = Vector3.up * hA; var upB = Vector3.up * hB;
            var foot = Vector3.down * KerbFaceM;
            bk.WallSloped(a, b, a.y + foot.y, a.y + hA, b.y + foot.y, b.y + hB, -outward, v0, v1, 0.3f, 0.45f);          // inner face
            bk.WallSloped(a + o, b + o, a.y + foot.y, a.y + hA, b.y + foot.y, b.y + hB, outward, v0, v1, 0.3f, 0.45f);  // outer face
            bk.Up(a + upA, b + upB, b + o + upB, a + o + upA,
                  new Vector2(0.45f, v0), new Vector2(0.45f, v1), new Vector2(0.5f, v1), new Vector2(0.5f, v0));
            var uv = new Vector2(0.3f, v0);
            if (capA) bk.Face(a + foot, a + upA, a + o + upA, a + o + foot, a - b, uv, uv, uv, uv);
            if (capB) bk.Face(b + foot, b + upB, b + o + upB, b + o + foot, b - a, uv, uv, uv, uv);
        }

        /// <summary>
        /// A rail along a drawn edge from <paramref name="a"/> to
        /// <paramref name="b"/>, into the barrier bucket (Solid layer, its own
        /// collider). A closed solid: the traffic face RailW inside the edge,
        /// the top, the outer face RailOverhangM outside it reaching
        /// <paramref name="drop"/> below the surface (a deck's fascia depth,
        /// or buried under a verge), an underside, and end caps where a run
        /// ends. It was a hollow 0.3 m shell of three faces on the ROAD layer
        /// with open ends: a wheel ray could land on its top, and nothing
        /// treated it as a wall. <paramref name="inA"/>/<paramref name="inB"/>
        /// point from the edge toward the traffic. <paramref name="sinkA"/>/
        /// <paramref name="sinkB"/> lower the traffic face's FOOT at each end
        /// (its top stays RailH over the road): a flared end stands over the
        /// verge falling away beside the road, and with its foot at tarmac
        /// height it showed a slit of daylight a hand deep under the tip.
        /// </summary>
        static void EmitRail(Vector3 a, Vector3 b, Vector2 inA, Vector2 inB, float drop, bool capA, bool capB, float v0, float v1,
                             float sinkA = 0f, float sinkB = 0f, float overhangA = RailOverhangM, float overhangB = RailOverhangM,
                             float hA = RailH, float hB = RailH)
        {
            var bk = barrierBucket;
            var upA = Vector3.up * hA; var upB = Vector3.up * hB;
            var down = Vector3.down * drop;
            Vector3 iA = a + Flat(inA) * RailW, iB = b + Flat(inB) * RailW;
            Vector3 fA = iA + Vector3.down * sinkA, fB = iB + Vector3.down * sinkB;   // the traffic face's feet
            Vector3 oA = a - Flat(inA) * overhangA, oB = b - Flat(inB) * overhangB;
            var inAvg = Flat(inA + inB);
            bk.Face(fA, iA + upA, iB + upB, fB, inAvg,
                    new Vector2(v0, 0.3f), new Vector2(v0, 0.45f), new Vector2(v1, 0.45f), new Vector2(v1, 0.3f));
            bk.Up(iA + upA, oA + upA, oB + upB, iB + upB,
                  new Vector2(0.45f, v0), new Vector2(0.5f, v0), new Vector2(0.5f, v1), new Vector2(0.45f, v1));
            bk.Face(oA + down, oA + upA, oB + upB, oB + down, -inAvg,
                    new Vector2(v0, 0.2f), new Vector2(v0, 0.45f), new Vector2(v1, 0.45f), new Vector2(v1, 0.2f));
            bk.Face(fA, oA + down, oB + down, fB, Vector3.down,
                    new Vector2(0.45f, v0), new Vector2(0.5f, v0), new Vector2(0.5f, v1), new Vector2(0.45f, v1));
            // the end caps wear the concrete as the faces do (leftover item 2:
            // they were one texel, a flat grey square at every rail end)
            float cw = (RailW + overhangA) / RoadVTile, cwB = (RailW + overhangB) / RoadVTile;
            if (capA) bk.Face(fA, iA + upA, oA + upA, oA + down, a - b,
                              new Vector2(v0, 0.3f), new Vector2(v0, 0.45f), new Vector2(v0 + cw, 0.45f), new Vector2(v0 + cw, 0.2f));
            if (capB) bk.Face(fB, iB + upB, oB + upB, oB + down, b - a,
                              new Vector2(v1, 0.3f), new Vector2(v1, 0.45f), new Vector2(v1 + cwB, 0.45f), new Vector2(v1 + cwB, 0.2f));
        }

        /// <summary>The W-beam's front-line offset AWAY from the road at
        /// <paramref name="u"/> metres from its parapet end: on the parapet's
        /// line along the transition, then the terminal's straight flare.</summary>
        static float WBeamOut(float u) => u <= TaperLenM ? 0f : WBeamFlareM * Mathf.Clamp01((u - TaperLenM) / WBeamTermM);
        /// <summary>The beam's top over the road edge at <paramref name="u"/>:
        /// WBeamTopM, then turned down into the ground over the last WBeamDownM.</summary>
        static float WBeamTop(float u)
        {
            float t = (u - (WBeamLenM - WBeamDownM)) / WBeamDownM;
            return t <= 0f ? WBeamTopM : Mathf.Lerp(WBeamTopM, -0.15f, Mathf.Clamp01(t));
        }

        /// <summary>
        /// One span's share of a W-beam lead-in (leftover item 2), from arc
        /// distance <paramref name="uA"/> at section A to <paramref name="uB"/>
        /// at B (from its parapet end): its pieces and posts for CityPoles to
        /// draw (<see cref="TileMeshes.wbeams"/>), and its collider - a closed
        /// box from the beam's face back past its posts, from under the
        /// verge to the beam's top (the turned-down tip goes into the
        /// ground with it, so no face stands square to the traffic).
        /// </summary>
        static void EmitWBeam(TileMeshes tm, Section A, Section B, int side, float uA, float uB)
        {
            float u0 = Mathf.Max(0f, Mathf.Min(uA, uB)), u1 = Mathf.Min(WBeamLenM, Mathf.Max(uA, uB));
            if (u1 - u0 < 0.01f || Mathf.Abs(uB - uA) < 1e-4f) return;
            Vector3 eA = A.Edge(side), eB = B.Edge(side);
            Vector2 oA = A.Out(side), oB = B.Out(side);
            void At(float u, out Vector3 front, out Vector2 outw, out float yEdge)
            {
                float t = Mathf.Clamp01((u - uA) / (uB - uA));
                var ed = Vector3.Lerp(eA, eB, t);
                outw = Vector2.Lerp(oA, oB, t).normalized;
                yEdge = ed.y;
                float o = WBeamOut(u) - (RailW + WBeamInsetM);
                front = ed + new Vector3(outw.x, 0f, outw.y) * o;
            }
            // pieces: at most WBeamStepM, split where the transition ends and
            // where the turn-down starts
            var us = wbStations; us.Clear(); us.Add(u0); us.Add(u1);
            int pieces = Mathf.Max(1, Mathf.CeilToInt((u1 - u0) / WBeamStepM));
            for (int q = 1; q < pieces; q++) us.Add(Mathf.Lerp(u0, u1, (float)q / pieces));
            foreach (float brk in WBeamBreaks) if (brk > u0 + 0.01f && brk < u1 - 0.01f) us.Add(brk);
            us.Sort();
            for (int q = 1; q < us.Count; q++)
            {
                float uPrev = us[q - 1], uNext = us[q];
                if (uNext - uPrev < 0.01f) continue;
                At(uPrev, out var fa, out var na, out float ya);
                At(uNext, out var fb, out var nb, out float yb);
                float ta = ya + WBeamTop(uPrev), tb = yb + WBeamTop(uNext);
                tm.wbeams.Add(new WBeamPiece
                {
                    a = fa, b = fb, outA = na, outB = nb,
                    botA = ta - WBeamDepthM, topA = ta, botB = tb - WBeamDepthM, topB = tb,
                    endA = uPrev <= 0.01f, endB = uNext >= WBeamLenM - 0.01f,
                });
                // the collider: the drawn beam itself (CityPoles.EmitWBeam) -
                // its face, back, top, underside and two ends - and nothing
                // the eye cannot see (INVISIBLE COLLIDERS, 2026-10-04: it was
                // a 0.45 m box from half a metre under the verge to the top,
                // a wall under the beam and behind it with no drawn face)
                var bk = guardBucket;
                var na3 = new Vector3(na.x, 0f, na.y); var nb3 = new Vector3(nb.x, 0f, nb.y);
                // the face on the W's middle (its ridges 2.5 cm in front, its valleys behind)
                Vector3 fcA = fa + na3 * WBeamFaceM, fcB = fb + nb3 * WBeamFaceM;
                Vector3 baA = fa + na3 * WBeamBackM, baB = fb + nb3 * WBeamBackM;
                float lowA = ta - WBeamDepthM, lowB = tb - WBeamDepthM;
                bk.WallSloped(fcA, fcB, lowA, ta, lowB, tb, -(na + nb), 0f, 1f, 0f, 1f);
                bk.WallSloped(baA, baB, lowA, ta, lowB, tb, na + nb, 0f, 1f, 0f, 1f);
                bk.Up(new Vector3(fcA.x, ta, fcA.z), new Vector3(fcB.x, tb, fcB.z), new Vector3(baB.x, tb, baB.z), new Vector3(baA.x, ta, baA.z),
                      Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                bk.Down(new Vector3(fcA.x, lowA, fcA.z), new Vector3(fcB.x, lowB, fcB.z), new Vector3(baB.x, lowB, baB.z), new Vector3(baA.x, lowA, baA.z),
                        Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                var along = new Vector3(fb.x - fa.x, 0f, fb.z - fa.z);
                bk.Face(new Vector3(fa.x, lowA, fa.z), new Vector3(fa.x, ta, fa.z), new Vector3(baA.x, ta, baA.z), new Vector3(baA.x, lowA, baA.z),
                        -along, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                bk.Face(new Vector3(fb.x, lowB, fb.z), new Vector3(fb.x, tb, fb.z), new Vector3(baB.x, tb, baB.z), new Vector3(baB.x, lowB, baB.z),
                        along, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            }
            // posts, at their stations from the parapet end (one span owns
            // each: (u0, u1]); none inside the concrete's taller half
            for (int m = Mathf.FloorToInt(u0 / WBeamPostM) + 1; m * WBeamPostM <= u1 + 1e-4f; m++)
            {
                float u = m * WBeamPostM;
                if (u < 0.5f * TaperLenM || WBeamTop(u) < 0.35f) continue;
                At(u, out var fp, out var np, out float yp);
                tm.wbeamPosts.Add(new WBeamPost { at = fp, outward = np, top = yp + WBeamTop(u) - 0.03f });
            }
        }
        /// <summary>The drawn W-beam's back, metres behind its face
        /// (CityPoles.WBeamBackO): the collider's depth.</summary>
        const float WBeamBackM = 0.08f;
        /// <summary>The collider's face, metres behind the drawn W's front
        /// ridges (CityPoles.WBeamProfO runs 0 to 5 cm): its middle.</summary>
        const float WBeamFaceM = 0.025f;
        /// <summary>The W-beam's corridor, metres out from the drawn edge, that
        /// must be clear of every other road's pavement (its flare, posts and
        /// box reach 1.4 m), and the pad kept from that pavement: past the
        /// other road's own verge line, so no beam crosses a car running off
        /// it.</summary>
        static readonly float[] WBeamCorridorM = { 0.4f, 1.0f, 1.7f };
        const float WBeamRoadPadM = 1.3f;
        static readonly HashSet<int> wbRoadScratch = new HashSet<int>();

        /// <summary>Is the W-beam's corridor beside this span clear of every
        /// other road (from the map alone - its centreline and width - so every
        /// tile decides the same)? Roads more than 2.5 m above or below pass.</summary>
        static bool WBeamCorridorClear(CityMap map, CityMap.Edge e, Vector3 origin, Section A, Section B, int side)
        {
            for (int q = 0; q <= 2; q++)
            {
                float t = q * 0.5f;
                var ed = Vector3.Lerp(A.Edge(side), B.Edge(side), t) + origin;
                var outw = Vector2.Lerp(A.Out(side), B.Out(side), t).normalized;
                foreach (float d in WBeamCorridorM)
                    if (OtherRoadAt(map, e, new Vector2(ed.x, ed.z) + outw * d, ed.y)) return false;
            }
            return true;
        }

        static bool OtherRoadAt(CityMap map, CityMap.Edge e, Vector2 p, float y)
        {
            wbRoadScratch.Clear();
            map.EdgeSegsInRect(p - Vector2.one * 20f, p + Vector2.one * 20f, wbRoadScratch);
            foreach (int packed in wbRoadScratch)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                if (ei == e.index) continue;
                var o = map.edges[ei];
                if (si + 1 >= o.pts.Length) continue;
                Vector2 a = o.pts[si], dd = o.pts[si + 1] - a;
                float L2 = dd.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, dd) / L2) : 0f;
                if (Vector2.Distance(p, a + dd * t) > o.width * 0.5f + WBeamRoadPadM) continue;
                if (Mathf.Abs(o.YAt(o.s[si] + Mathf.Sqrt(L2) * t) - y) > 2.5f) continue;
                return true;
            }
            return false;
        }
        static readonly float[] WBeamBreaks = { TaperLenM, WBeamLenM - WBeamDownM };
        static readonly List<float> wbStations = new List<float>(16);

        /// <summary>A deck span's box: both fascias facing out, the soffit
        /// facing down. Its rails are the side's business (EmitSide).</summary>
        static void EmitDeckBox(Bucket con, Section A, Section B, float v0, float v1, bool noFasciaL = false, bool noFasciaR = false)
        {
            float dk = CityElevation.DeckThick;
            var dAL = A.L + Vector3.down * dk; var dAR = A.R + Vector3.down * dk;
            var dBL = B.L + Vector3.down * dk; var dBR = B.R + Vector3.down * dk;
            if (!noFasciaL) con.WallSloped(A.L, B.L, dAL.y, A.L.y, dBL.y, B.L.y, -A.right, v0, v1, 0f, 0.15f);
            if (!noFasciaR) con.WallSloped(A.R, B.R, dAR.y, A.R.y, dBR.y, B.R.y, A.right, v0, v1, 0f, 0.15f);
            con.Down(dAR, dAL, dBL, dBR, new Vector2(0, v0), new Vector2(1, v0), new Vector2(1, v1), new Vector2(0, v1));
        }

        // ------------------------------------------------------------------
        //  Strips: the verge beside a grounded edge, the solid ground behind a
        //  retaining wall, half a squeeze strip. One cross-section every
        //  VergeStepM, each solved against the finished lattice.
        // ------------------------------------------------------------------
        struct StripShape
        {
            /// <summary>Metres out from the drawn edge the strip starts (a
            /// wall's thickness), and the height of its first point over the
            /// tarmac edge for a flat strip. A verge starts AT the edge,
            /// RoadsideRules.EdgeDropM down.</summary>
            public float start, rise;
            public float shoulder, maxRun;
            /// <summary>No cross-fall and no foreslope: level until it meets
            /// the lattice.</summary>
            public bool flat;
            /// <summary>Exactly maxRun wide, no search, no tuck.</summary>
            public bool fixedRun;
            /// <summary>A fixed flat strip laid UNDER another road's pavement:
            /// never higher than an inch below the road it runs into, so a
            /// branch a few decimetres above its host cannot stand it on the
            /// host's lanes (see SeamCeiling). <see cref="ownEdge"/> is the
            /// edge laying it.</summary>
            public bool seam;
            /// <summary>A seam laid on a deck, between two decks: held under
            /// the pavement at its own level only (SeamCeiling).</summary>
            public bool deckSeam;
            public int ownEdge;
            /// <summary>Laid along a line between two roads' corners — a fan's
            /// open chord, a gore nose — whose ends are solved against
            /// different arms: a triangle standing steeper than
            /// <see cref="FillSteepNy"/> between them is a fin, not a slope,
            /// and is left out (a 1.2 m one stood 0.8 m off e7753's edge where
            /// it meets e9968 1.35 m higher).</summary>
            public bool noFins;
            /// <summary>Half a squeeze strip: at least <see cref="maxRun"/>
            /// wide, and at every cross-section a little past the middle of
            /// the gap to the pavement drawn beside it (see SolveStrip).</summary>
            public bool halfGap;
        }

        static readonly Vector3[] profPrev = new Vector3[4], profCur = new Vector3[4];

        /// <summary>Lay a strip along a drawn edge (tile-local end points at
        /// tarmac height, outward unit vectors in map view).</summary>
        static void EmitStrip(CityMap map, Trims trims, TileMeshes tm, Vector3 a, Vector3 b, Vector2 outA, Vector2 outB, StripShape sh)
        {
            var o = tm.origin;
            var pa = new Vector2(a.x + o.x, a.z + o.z); var pb = new Vector2(b.x + o.x, b.z + o.z);
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(pa, pb) / VergeStepM));
            GatherNear(map, pa, pb, sh.start + sh.maxRun + RoadsideRules.ToeTuckRunM);
            string tagWas = groundTag;
            if (groundLog != null) groundTag += sh.seam ? " seam" : sh.halfGap ? " half strip" : sh.flat ? " flat strip" : " verge";
            LayStrip(map, trims, o, pa, pb, a.y, b.y, outA, outB, sh, steps);
            groundTag = tagWas;
        }

        /// <summary>EmitStrip's cross-sections, quad by quad (world end
        /// points, tarmac heights, outward units).</summary>
        static void LayStrip(CityMap map, Trims trims, Vector3 o, Vector2 pa, Vector2 pb, float ya, float yb, Vector2 outA, Vector2 outB, StripShape sh, int steps)
        {
            bool havePrev = false;
            int prevTuck = -1;
            Bucket prevBk = null; int prevBase = -1;
            float tPrev = 0f;
            for (int j = 0; j <= steps; j++)
            {
                float t = (float)j / steps;
                bool ok = SolveStripAt(map, trims, pa, pb, ya, yb, outA, outB, sh, t, profCur);
                int curTuck = ok ? stripTuckEdge : -1;
                if (j > 0 && ok != havePrev)
                {
                    // A refused cross-section took the whole quad to its good
                    // neighbour with it: VergeStepM of nothing beside the edge
                    // wherever another road touches it for a hand's width (a
                    // 40 cm lip on North McDowell Street where East 10th Street
                    // leaves it). Close in on where the refusal starts and lay
                    // the strip up to there.
                    float tOk = ok ? t : tPrev, tBad = ok ? tPrev : t;
                    bool found = false;
                    for (int it = 0; it < StripRefineSteps; it++)
                    {
                        float tMid = 0.5f * (tOk + tBad);
                        if (SolveStripAt(map, trims, pa, pb, ya, yb, outA, outB, sh, tMid, profEdge))
                        {
                            tOk = tMid; found = true;
                            System.Array.Copy(profEdge, profNear, 4);
                        }
                        else tBad = tMid;
                    }
                    if (found)
                    {
                        if (ok) { System.Array.Copy(profNear, profPrev, 4); havePrev = true; prevBk = null; prevTuck = -1; tPrev = tOk; }
                        else LayStripQuad(map, o, profNear, sh.noFins, ref prevBk, ref prevBase);
                    }
                }
                if (ok && havePrev)
                {
                    int tuck = curTuck >= 0 ? curTuck : prevTuck;
                    if (tuck >= 0) LayTucked(map, trims, o, pa, pb, ya, yb, outA, outB, sh, tuck, tPrev, t, profCur, 0, ref prevBk, ref prevBase);
                    else LayStripQuad(map, o, profCur, sh.noFins, ref prevBk, ref prevBase);
                }
                else prevBk = null;
                if (ok) System.Array.Copy(profCur, profPrev, 4);
                havePrev = ok;
                prevTuck = curTuck;
                tPrev = t;
            }
        }

        /// <summary>
        /// One quad of a strip that tucks under another road's pavement
        /// (<paramref name="tuckEdge"/>) at either end, from
        /// <see cref="profPrev"/> (at <paramref name="tA"/>) to
        /// <paramref name="profB"/>: halved, down to
        /// <see cref="TuckRefineDepth"/> halvings, where the straight quad's
        /// tuck would stand ON that pavement between its two cross-sections.
        /// Each cross-section tucks under the pavement where it meets it, and
        /// the pavement between need not be straight: where a branch is
        /// clipped against its host its edge climbs to the host's height
        /// within a metre, and Armory Drive's connector, tucked under Sam
        /// Ryburn Walk at both ends 2.5 m apart, stood as a grass wedge up to
        /// 0.3 m over the lane between them (the second WP-04 review). Halving
        /// every quad whose two tucks merely differed in height put ridges of
        /// a few centimetres between neighbouring roads' connectors at twenty
        /// junction corners, faces the body box met.
        /// </summary>
        static void LayTucked(CityMap map, Trims trims, Vector3 o, Vector2 pa, Vector2 pb, float ya, float yb, Vector2 outA, Vector2 outB, StripShape sh,
                              int tuckEdge, float tA, float tB, Vector3[] profB, int depth, ref Bucket prevBk, ref int prevBase)
        {
            float stepM = (tB - tA) * Vector2.Distance(pa, pb);
            if (depth < TuckRefineDepth && stepM > 2f * TuckRefineMinM && TuckOnPavement(map, trims, tuckEdge, profPrev, profB))
            {
                float tM = 0.5f * (tA + tB);
                var pm = tuckProf[depth];
                if (SolveStripAt(map, trims, pa, pb, ya, yb, outA, outB, sh, tM, pm))
                {
                    LayTucked(map, trims, o, pa, pb, ya, yb, outA, outB, sh, tuckEdge, tA, tM, pm, depth + 1, ref prevBk, ref prevBase);
                    LayTucked(map, trims, o, pa, pb, ya, yb, outA, outB, sh, tuckEdge, tM, tB, profB, depth + 1, ref prevBk, ref prevBase);
                    return;
                }
            }
            LayStripQuad(map, o, profB, sh.noFins, ref prevBk, ref prevBase);
        }

        /// <summary>Would the straight quad between two cross-sections stand
        /// on a ribbon's pavement, less than an inch under it? Read at the
        /// middles of its tuck points (2, 3) and of its quad between them.</summary>
        static bool TuckOnPavement(CityMap map, Trims trims, int edge, Vector3[] a, Vector3[] b)
        {
            var ol = OutlineOf(map, trims, edge);
            if (ol.L == null) return false;
            for (int k = 0; k < 3; k++)
            {
                var m = k == 2 ? (a[2] + a[3] + b[2] + b[3]) * 0.25f : (a[2 + k] + b[2 + k]) * 0.5f;
                float h = OutlineHeightAt(ol, new Vector2(m.x, m.z));
                if (!float.IsNaN(h) && m.y > h - RoadsideRules.EdgeDropM) return true;
            }
            return false;
        }

        /// <summary>Halvings LayTucked spends at most: VergeStepM / 8.</summary>
        const int TuckRefineDepth = 3;
        /// <summary>The shortest quad LayTucked lays by halving.</summary>
        const float TuckRefineMinM = 0.15f;
        static readonly Vector3[][] tuckProf = { new Vector3[4], new Vector3[4], new Vector3[4] };

        /// <summary>Bisections EmitStrip spends closing in on a refused
        /// cross-section: VergeStepM / 16, 16 cm.</summary>
        const int StripRefineSteps = 4;
        static readonly Vector3[] profEdge = new Vector3[4], profNear = new Vector3[4];

        /// <summary>A strip's cross-section a fraction <paramref name="t"/>
        /// along its edge (world end points, tarmac heights, outward units).</summary>
        static bool SolveStripAt(CityMap map, Trims trims, Vector2 pa, Vector2 pb, float ya, float yb,
                                 Vector2 outA, Vector2 outB, StripShape sh, float t, Vector3[] prof)
        {
            var outw = Vector2.Lerp(outA, outB, t);
            outw = outw.sqrMagnitude > 1e-6f ? outw.normalized : outA;
            return SolveStrip(map, trims, Vector2.Lerp(pa, pb, t), Mathf.Lerp(ya, yb, t), outw, sh, prof);
        }

        /// <summary>One quad of a strip, from <see cref="profPrev"/> to
        /// <paramref name="prof"/>, which then becomes profPrev. ONE vertex per
        /// profile point, shared along the strip: a verge is most of a tile's
        /// ground triangles now, and the ground collider is cooked on every
        /// tile build.</summary>
        static void LayStripQuad(CityMap map, Vector3 o, Vector3[] prof, bool noFins, ref Bucket prevBk, ref int prevBase)
        {
            var cen = (profPrev[1] + profPrev[2] + prof[1] + prof[2]) * 0.25f;
            bool paved = PavedAt(map, cen.x, cen.z);
            var bk = GroundBucket(paved);
            if (bk != prevBk) { prevBase = AddProfile(bk, profPrev, o, paved); prevBk = bk; }
            int curBase = AddProfile(bk, prof, o, paved);
            // Beside a profile collapsed onto the edge (the next road's
            // pavement is there) the other profile's toe band fans from that
            // one point down its tuck: where that tuck is vertical (a
            // connector, a strip stopped by a road) the triangle is a fin a
            // tuck deep. The band is under the strip's top everywhere else, so
            // it is left out there.
            int bands = Collapsed(profPrev) || Collapsed(prof) ? 2 : 3;
            for (int q = 0; q < bands; q++)
            {
                StripTris(bk, prevBase + q, prevBase + q + 1, curBase + q + 1, curBase + q, noFins);
                if (groundLog != null)
                {
                    groundLog.Add((groundTag + $" band{q}", profPrev[q], profPrev[q + 1], prof[q + 1]));
                    groundLog.Add((groundTag + $" band{q}", profPrev[q], prof[q + 1], prof[q]));
                }
            }
            prevBase = curBase;
            System.Array.Copy(prof, profPrev, 4);
        }

        static bool Collapsed(Vector3[] prof) =>
            (prof[3] - prof[0]).sqrMagnitude < 1e-6f && (prof[1] - prof[0]).sqrMagnitude < 1e-6f;

        static int AddProfile(Bucket bk, Vector3[] prof, Vector3 origin, bool paved)
        {
            int start = bk.v.Count;
            for (int q = 0; q < 4; q++)
            {
                bk.v.Add(prof[q] - origin);
                bk.uv.Add(GroundUV(paved, prof[q].x, prof[q].z));
            }
            return start;
        }

        /// <summary>A quad of shared vertices facing up, as Bucket.Up orders
        /// it; skipped where it has no area at all (a flat strip's shoulder),
        /// and with <paramref name="noFins"/> each triangle standing steeper
        /// than <see cref="FillSteepNy"/> left out.</summary>
        static void StripTris(Bucket bk, int ia, int ib, int ic, int id, bool noFins = false)
        {
            Vector3 a = bk.v[ia], b = bk.v[ib], c = bk.v[ic], d = bk.v[id];
            if ((b - a).sqrMagnitude < 1e-6f && (c - d).sqrMagnitude < 1e-6f) return;
            float area = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z)
                       + (c.x - a.x) * (d.z - a.z) - (d.x - a.x) * (c.z - a.z);
            if (area < 0f) { int t = ib; ib = id; id = t; b = bk.v[ib]; d = bk.v[id]; }
            if (!noFins || !Fin(a, c, b)) { bk.t.Add(ia); bk.t.Add(ic); bk.t.Add(ib); }
            if (!noFins || !Fin(a, d, c)) { bk.t.Add(ia); bk.t.Add(id); bk.t.Add(ic); }
        }

        /// <summary>Does a triangle stand steeper than <see cref="FillSteepNy"/>?</summary>
        static bool Fin(Vector3 a, Vector3 b, Vector3 c)
        {
            var n = Vector3.Cross(b - a, c - a);
            float m = n.magnitude;
            return m > 1e-6f && Mathf.Abs(n.y) < FillSteepNy * m;
        }

        /// <summary>The steepest a fill or a chord's strip triangle may stand:
        /// its normal's vertical share, the roadside audit's face threshold
        /// (1V:1H).</summary>
        const float FillSteepNy = 0.7f;

        /// <summary>
        /// One cross-section of a strip, world space, into
        /// <paramref name="prof"/>: its first point, the end of the
        /// shoulder, where it crosses the lattice, and its toe tucked
        /// RoadsideRules.ToeTuckM under the lattice RoadsideRules.ToeTuckRunM
        /// further out — so the strip and the lattice CROSS instead of
        /// abutting, and a car rides the higher of two continuous surfaces.
        /// A verge falls at the shoulder's cross-fall, then 1V:4H (WP-14: the
        /// fill's slope; 1V:6H across the clear zone before); it never runs
        /// over another road's paved width. False where there is no room for it at all.
        /// </summary>
        static bool SolveStrip(CityMap map, Trims trims, Vector2 edgeW, float yEdge, Vector2 outw, StripShape sh, Vector3[] prof)
            => SolveStrip(map, trims, edgeW, yEdge, outw, sh, prof, out _, out _);

        /// <param name="graded">The verge meets what lies beside the road the
        /// DOT way: it crosses the lattice inside its run, or it reaches the
        /// next road's pavement over a recoverable CONNECTOR, or that pavement
        /// is flush at the edge already. False where it ran out of room still
        /// above the ground (a drop grading cannot catch — a warranted barrier,
        /// RoadsideRules) or came to a road too far below to connect to.</param>
        /// <param name="stoppedBy">Where it was not graded because another
        /// road's pavement stopped it, that road's surface height; NaN
        /// otherwise. A step down onto a road is not a fall off a roadside.</param>
        static bool SolveStrip(CityMap map, Trims trims, Vector2 edgeW, float yEdge, Vector2 outw, StripShape sh, Vector3[] prof, out bool graded, out float stoppedBy)
        {
            graded = false;
            stoppedBy = float.NaN;
            stripTuckEdge = -1;
            var s0 = edgeW + outw * sh.start;
            float y0 = sh.flat ? yEdge + sh.rise : yEdge - RoadsideRules.EdgeDropM;
            if (sh.fixedRun)
            {
                graded = true;
                float width = sh.maxRun;
                if (sh.halfGap)
                {
                    // HALF THE GAP AS DRAWN HERE. The strip's width is read at
                    // the ribbon's sections, and between them the two drawn
                    // edges part: two half strips 15 cm wide left a slot 0.7 m
                    // wide and 22 cm deep between North Tryon Street's
                    // carriageways, a wheel's drop past a hand of shoulder. Each
                    // half reaches a little past the middle of the gap to the
                    // pavement really beside it, never onto that pavement —
                    // and under a pavement no lower than itself, on to its edge:
                    // the half there may be a retaining face instead, and the
                    // sliver short of it stood open to the lattice a metre down
                    // (I-277's e2347 beside e2345, 46 cm higher).
                    //
                    // Where the drawn edges come closer than the strip read at
                    // the sections, a LOWER pavement bounds it as well: the
                    // half as read lay on that road's lanes, grass 5 cm up
                    // across East 3rd Street's inside lane where e10366's half
                    // strip crossed e10365, drawn out of the same node beneath
                    // it. The lower road's own half reaches on to this edge,
                    // under it, so the gap stays floored.
                    //
                    // The pavement beside is looked for across the squeeze's
                    // whole height band, not a verge's half metre: a road more
                    // than half a metre up was "a deck the ground passes under",
                    // so the lower half stopped at 15 cm and a slot a quarter of
                    // a metre wide stood open to the lattice 0.9 m down between
                    // it and the retaining face its neighbour stands on (I-77's
                    // e1891 beside e1877, 74 cm higher, and 56 more along I-77
                    // and I-277). Nothing a car passes under stands that close
                    // above a road (Squeeze splits the pair instead).
                    ClearRun(map, trims, s0, outw, y0, HalfGapReachM, out float gapY, out float gapRun, out _,
                             RoadsideRules.CarBandM + CityElevation.DeckThick);
                    if (!float.IsNaN(gapRun))
                        width = gapY - RoadsideRules.EdgeDropM >= y0
                            ? Mathf.Max(width, gapRun - PavedInsetM + HalfGapUnderM)
                            : Mathf.Min(Mathf.Max(width, 0.5f * gapRun + HalfGapOverlapM), gapRun - PavedInsetM);
                }
                if (width < 0.01f) return false;
                float yFar = y0;
                if (sh.seam)
                {
                    // and it falls away as it goes under: the strip is straight
                    // between samples 2.5 m apart and the road above it bends at
                    // its stations, so a flat inch cleared its lanes by less
                    // than the sag between them
                    SeamCeiling(map, trims, s0, outw, width, sh.ownEdge, sh.deckSeam, y0, seamCeil);
                    y0 = Mathf.Min(y0, seamCeil[0]);
                    // Not over a junction's fan: where the fan reaches under
                    // this edge it floors the gap itself, and a seam laid there
                    // stood on its lanes (3 cm of grass across North College
                    // Street's mouth at node 1482, e23368's seam over a fan
                    // 13 cm below it). Held under the fan instead, a cross-
                    // section's start dropped under the edge beside it, and the
                    // quad to the next one stood 11 cm under that edge.
                    if (StartsOnFan(map, trims, s0, outw, width, y0 + RoadsideRules.EdgeDropM)) return false;
                    yFar = Mathf.Min(Mathf.Min(y0 - SeamFallM, seamCeil[2]), 2f * seamCeil[1] - y0);
                }
                var e1 = s0 + outw * width;
                // A half strip starts under its own pavement's edge, not on
                // it: two meshes meeting edge-on along one line let a ray
                // exactly on it through both, down to the lattice 0.4 m below
                // (North Tryon Street's e18531 beside e10905).
                var b0 = sh.halfGap ? s0 - outw * PavedInsetM : s0;
                prof[0] = new Vector3(b0.x, y0, b0.y);
                if (sh.seam && width > 2f * SeamDropRunM)
                {
                    // UNDERLAPS (plan L3 / A3 FD8): the inch-down seam showed
                    // in the crack, and then ran on 2.5-8 cm under the other
                    // surface for its whole width - two surfaces that close
                    // flicker at range. It drops SeamUnderM under that surface
                    // within SeamDropRunM of the crack and stays there.
                    float yNear = Mathf.Min(y0 + RoadsideRules.EdgeDropM, seamCeil[0]) - SeamUnderM;
                    float yEnd = Mathf.Min(Mathf.Min(yNear, Mathf.Min(yFar, seamCeil[2] - SeamUnderM)), 2f * (seamCeil[1] - SeamUnderM) - yNear);
                    var en = s0 + outw * SeamDropRunM;
                    prof[1] = new Vector3(en.x, yNear, en.y);
                    prof[2] = prof[3] = new Vector3(e1.x, yEnd, e1.y);
                }
                else prof[1] = prof[2] = prof[3] = new Vector3(e1.x, yFar, e1.y);
                return true;
            }
            // measured from the strip's own first point: a road "more than half
            // a metre above" is a deck the strip passes under only if it is
            // above THE STRIP, and a wall-top shelf is 0.81 m over the tarmac
            float run = ClearRun(map, trims, s0, outw, y0, sh.maxRun, out float blockedBy, out float edgeRun, out bool intoFan);
            int blockEdge = clearRunBlockEdge;
            bool blocked = !float.IsNaN(blockedBy);
            float yOther = blockedBy - RoadsideRules.EdgeDropM;
            // A CONNECTOR to the road beside: the ground between two roads
            // closer than a verge's run is graded from one pavement's edge to
            // the other's, where the grade between them is recoverable. It was
            // a verge down to the other road's pad and a vertical tuck there,
            // with the lattice sunk between the two tucks: a slot beside every
            // pair of close parallel streets, and a 13 cm face (North McDowell
            // beside its own other carriageway) that the body box met coming
            // back from the neighbour's shoulder.
            bool Connect(float slope)
            {
                // not onto a junction's fan: its triangles are planar between
                // the node and the arm corners, and a connector tucked under
                // the arm's own edge line stood on the fan wherever the fan
                // dips below that line (35 cm of grass on Armory Drive's)
                if (!blocked || sh.flat || float.IsNaN(edgeRun) || intoFan) return false;
                float eEnd = Mathf.Max(edgeRun, 0f) + ConnectorTuckInM;
                // a flush shoulder first (its cross-fall, toward the other road
                // whichever way that is), then the grade
                float eS = Mathf.Min(sh.shoulder, eEnd * 0.25f);
                float yS = y0 + Mathf.Sign(yOther - y0) * Mathf.Min(RoadsideRules.ShoulderCrossFall * eS, Mathf.Abs(yOther - y0));
                if (Mathf.Abs(yOther - yS) > slope * (eEnd - eS) + RoadsideRules.EdgeDropM) return false;
                var q1 = s0 + outw * eS; var q2 = s0 + outw * eEnd;
                prof[0] = new Vector3(s0.x, y0, s0.y);
                prof[1] = new Vector3(q1.x, yS, q1.y);
                prof[2] = new Vector3(q2.x, yOther, q2.y);
                // the toe tucks on in under that pavement at a verge toe's own
                // slope: straight down it was a tuck-deep face wherever the
                // other road is drawn short of where the solve read it, and
                // every quad reaching it from the next profile stood vertical
                // (a 10 cm fin 4 cm off e5252's edge)
                var q3 = s0 + outw * (eEnd + RoadsideRules.ToeTuckRunM);
                prof[3] = new Vector3(q3.x, yOther - RoadsideRules.ToeTuckM, q3.y);
                // ...UNDER that pavement where the tuck lies, not at its height
                // where the grade met it. A branch clipped against its host
                // takes the host's height on its clipped edge and falls to its
                // own within a metre of warped ribbon; a host's connector that
                // met the branch at the high corner, on a ray crossing it at a
                // slant, put its toe 0.4 m on over pavement 0.3 m lower (Armory
                // Drive beside Sam Ryburn Walk, 0.3 m apart since WP-04).
                // LayTucked keeps the quads between two cross-sections under
                // it as well.
                if (blockEdge >= 0)
                {
                    var ol = OutlineOf(map, trims, blockEdge);
                    float h2 = OutlineHeightAt(ol, q2), h3 = OutlineHeightAt(ol, q3);
                    if (!float.IsNaN(h2)) prof[2].y = Mathf.Min(prof[2].y, h2 - RoadsideRules.EdgeDropM);
                    if (!float.IsNaN(h3)) prof[3].y = Mathf.Min(prof[3].y, h3 - RoadsideRules.ToeTuckM);
                    prof[3].y = Mathf.Min(prof[3].y, prof[2].y - RoadsideRules.ToeTuckM);
                }
                stripTuckEdge = blockEdge;
                return true;
            }
            if (run < 0.05f)
            {
                // the next road's pavement is at the edge already
                if (!blocked) return false;
                if (Connect(RoadsideRules.SteepestRecoverableSlope)) { graded = true; return true; }
                graded = !sh.flat && y0 - yOther <= RoadsideRules.EdgeDropFailM;
                if (!graded)
                {
                    stoppedBy = blockedBy;
                    // the same steep connector the search below falls back
                    // to: refused, this sample took both neighbouring quads
                    // with it, and two ramps a pad apart and 13 cm apart in
                    // height (e1229 beside e2852) lost five metres of verge to
                    // a 35 cm slot
                    return Connect(SteepConnectorSlope);
                }
                if (sh.flat || blockedBy < y0 + 0.01f) return false;
                // No room for a verge (the next road's pavement is at the
                // edge, and no lower than it): the profile collapses onto the
                // edge and the strip TAPERS to nothing under that pavement.
                // Refused, it took both neighbouring quads with it — five
                // metres of verge beside every such sample, down to the
                // lattice. Where the next road is lower the taper would stand
                // on its lanes, and the sample is still refused.
                prof[0] = prof[1] = prof[2] = prof[3] = new Vector3(s0.x, y0, s0.y);
                return true;
            }
            float shoulder = sh.flat ? 0f : sh.shoulder;
            // WP-14: past the shoulder the verge falls at the FILL's slope
            // (RoadsideRules.CityFillSlope, 1V:4H, the plan's recoverable
            // foreslope inside the clear zone), the slope the land itself is
            // graded to below the bench. At 1V:6H across the clear zone it
            // stayed above a fill's lattice - whose chord across the bench's
            // edge sags under the section - for eight metres and more, and a
            // street along a creek valley stood a rail on a retaining face
            // over a graded 1V:4H bank (Bryant Street). On level ground it
            // meets the lattice 0.1 m sooner.
            float Yv(float e) => sh.flat ? y0
                : y0 - RoadsideRules.ShoulderCrossFall * Mathf.Min(e, shoulder)
                     - RoadsideRules.CityFillSlope * Mathf.Max(0f, e - shoulder);
            float Lat(float e) { var q = s0 + outw * e; return LatticeY(map, q.x, q.y); }

            float ec = -1f, prevGap = Yv(0f) - Lat(0f);
            if (prevGap <= 0f) ec = 0f;
            else
                for (float e = VergeSearchStepM; e <= run + 1e-3f; e += VergeSearchStepM)
                {
                    float g = Yv(e) - Lat(e);
                    if (g <= 0f) { ec = e - VergeSearchStepM * g / (g - prevGap); break; }
                    prevGap = g;
                }
            if (ec < 0f)
            {
                // Ground behind a wall that meets another road before the
                // hill is not the hill: the strip between two roads is left
                // to their verges rather than ended in a wall-high face
                // beside the other road's shoulder.
                if (sh.flat && blocked) return false;
                if (Connect(RoadsideRules.SteepestRecoverableSlope)) { graded = true; return true; }
                if (blocked) stoppedBy = blockedBy;
                // Too steep to recover on, but still ground a car can cross: a
                // STEEP connector rather than a vertical tuck. Two ramps side by
                // side half a metre apart in height and a metre apart in plan
                // stood a 40 cm face between them for the body box to meet.
                if (Connect(SteepConnectorSlope)) return true;
                ec = run;
                // UNDER A HIGHER ROAD it carries on to that road's edge and
                // tucks beneath it. Stopped a pad short, it left a slot a
                // fifth of a metre wide down to the lattice at the foot of the
                // retaining face that road stands on (e9986's verge 40 cm
                // under e5252's edge); the pad is what keeps a verge off
                // LOWER lanes it would lie on.
                if (blocked && !float.IsNaN(edgeRun) && edgeRun > run && yOther >= Yv(edgeRun)) ec = edgeRun;
            }
            else graded = true;
            float eShoulder = Mathf.Min(shoulder, ec);
            // stopped by another road: tuck straight down, not on into its
            // pad; under a higher one, a hand in beneath its edge
            float eToe = !float.IsNaN(blockedBy) && ec >= run - 1e-3f
                ? (ec > run + 1e-3f ? ec + ConnectorTuckInM : ec)
                : ec + RoadsideRules.ToeTuckRunM;
            var p1 = s0 + outw * eShoulder; var p2 = s0 + outw * ec; var p3 = s0 + outw * eToe;
            float yc = Yv(ec);
            prof[0] = new Vector3(s0.x, y0, s0.y);
            prof[1] = new Vector3(p1.x, Yv(eShoulder), p1.y);
            prof[2] = new Vector3(p2.x, yc, p2.y);
            prof[3] = new Vector3(p3.x, Mathf.Min(yc, Lat(eToe)) - RoadsideRules.ToeTuckM, p3.y);
            return true;
        }

        /// <summary>The highest a seam strip from <paramref name="p"/> along
        /// <paramref name="outw"/> may stand at its start, middle and end
        /// (<paramref name="ceil"/>, float.MaxValue where nothing is over it):
        /// an inch under the lowest other grounded surface whose pavement (and
        /// pad) it crosses there — the host a branch runs beside, the branch
        /// beside a host, a fork's other arm most of a metre below, a painted
        /// gore. Its own road is skipped by index: a clipped inner vertex can
        /// lie past its own centreline, and a test from there read the road
        /// laying the strip as the road it runs under.
        ///
        /// Read from the surfaces as DRAWN (<see cref="OutlineOf"/>), point by
        /// point. Held under the lowest SOLVED height across all three, the
        /// seam met a ribbon drawn a few centimetres lower edge-on and stood
        /// level with it, never saw a gore, and let one falling 9% across a
        /// ramp pull its first centimetres a gore's fall under the edge it
        /// starts from (lips of 6-17 cm beside e172 and East 13th Street).
        ///
        /// A seam laid between two DECKS (<paramref name="onDeck"/>, EmitSide)
        /// is held under the pavement at its own level instead, within
        /// AttachDy of <paramref name="yRef"/>, decks and fans on structure
        /// included: it runs under the other deck's lanes, and a road passing
        /// under the bridge is not over it. (Held under the lowest pavement it
        /// crossed, a seam over I-77 fell 2.7 m to its lanes.)</summary>
        static void SeamCeiling(CityMap map, Trims trims, Vector2 p, Vector2 outw, float run, int ownEdge, bool onDeck, float yRef, float[] ceil)
        {
            GatherPavement(map, trims);
            bool Holds(bool structure, float hy) =>
                onDeck ? Mathf.Abs(hy - yRef) <= AttachDy : !structure;
            for (int q = 0; q <= 2; q++)
            {
                float lowest = float.MaxValue;
                var pt = p + outw * (run * q * 0.5f);
                // The first point is ON the edge laying the seam: only pavement
                // that is there holds it down. With the pad, a host a hand off
                // the edge and 8 cm lower pulled the seam's start an inch under
                // itself, a 9 cm lip (East 8th Street beside Louise Avenue);
                // the middle point still keeps the seam under that host.
                float pad = q == 0 ? PavedInsetM : VergeClearPadM;
                for (int k = 0; k < nearEdges.Count; k++)
                {
                    int oi = nearEdges[k];
                    var box = nearEdgeBox[k];
                    if (oi == ownEdge || pt.x < box.x - pad || pt.x > box.z + pad || pt.y < box.y - pad || pt.y > box.w + pad) continue;
                    var ol = OutlineOf(map, trims, oi);
                    if (ol.L == null || pt.x < ol.minX - pad || pt.x > ol.maxX + pad || pt.y < ol.minZ - pad || pt.y > ol.maxZ + pad) continue;
                    for (int i = 1; i < ol.L.Length; i++)
                    {
                        if (ol.BlockMisses(i, pt.x - pad, pt.y - pad, pt.x + pad, pt.y + pad)) { i += Outline.Block - 1; continue; }
                        bool deck = ol.deck[i];
                        Vector3 aL = ol.L[i - 1], bL = ol.L[i], bR = ol.R[i], aR = ol.R[i - 1];
                        if (pt.x < Mathf.Min(Mathf.Min(aL.x, bL.x), Mathf.Min(bR.x, aR.x)) - pad || pt.x > Mathf.Max(Mathf.Max(aL.x, bL.x), Mathf.Max(bR.x, aR.x)) + pad ||
                            pt.y < Mathf.Min(Mathf.Min(aL.z, bL.z), Mathf.Min(bR.z, aR.z)) - pad || pt.y > Mathf.Max(Mathf.Max(aL.z, bL.z), Mathf.Max(bR.z, aR.z)) + pad) continue;
                        if (!onDeck && deck) continue;   // a deck the seam passes under
                        // its sides moved OUT by the pad
                        float hy = float.NaN;
                        if (TriInterval(aL, bL, bR, -pad, 0f, 0f, pt, Vector2.right, 0f, out _, out _)) hy = TriHeight(aL, bL, bR, pt);
                        else if (TriInterval(aL, bR, aR, 0f, -pad, 0f, pt, Vector2.right, 0f, out _, out _)) hy = TriHeight(aL, bR, aR, pt);
                        if (!float.IsNaN(hy) && Holds(deck, hy)) lowest = Mathf.Min(lowest, hy);
                    }
                }
                // and the painted gores, drawn by this tile or the next
                foreach (var (a, b, c, d) in goreQuads)
                {
                    if (pt.x < Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x)) || pt.x > Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x)) ||
                        pt.y < Mathf.Min(Mathf.Min(a.z, b.z), Mathf.Min(c.z, d.z)) || pt.y > Mathf.Max(Mathf.Max(a.z, b.z), Mathf.Max(c.z, d.z))) continue;
                    float gy = float.NaN;
                    if (TriInterval(a, b, c, pt, Vector2.right, 0f, out _, out _)) gy = TriHeight(a, b, c, pt);
                    else if (TriInterval(a, c, d, pt, Vector2.right, 0f, out _, out _)) gy = TriHeight(a, c, d, pt);
                    if (!float.IsNaN(gy) && Holds(false, gy)) lowest = Mathf.Min(lowest, gy);
                }
                // and a grounded junction fan, past the first point: a seam
                // starting on a fan is not laid (StartsOnFan), and one running
                // onto a fan a hand past the edge goes under it as under a
                // ribbon — held over it, 4 cm of grass stood in North College
                // Street's mouth at node 1482
                if (q > 0)
                    foreach (int n in nearFans)
                    {
                        var fan = fanPolys[n];
                        float r = fan.reach + pad;
                        if ((pt - fan.centre).sqrMagnitude > r * r) continue;
                        bool onStructure = FanOnStructure(map, trims, n);
                        if (!onDeck && onStructure) continue;
                        var T = fan.tris;
                        for (int i = 0; i + 2 < T.Length; i += 3)
                            if (TriInterval(T[i], T[i + 1], T[i + 2], -pad, -pad, -pad, pt, Vector2.right, 0f, out _, out _))
                            {
                                float hy = TriHeight(T[i], T[i + 1], T[i + 2], pt);
                                if (Holds(onStructure, hy)) lowest = Mathf.Min(lowest, hy);
                            }
                    }
                ceil[q] = lowest < float.MaxValue ? lowest - RoadsideRules.EdgeDropM : float.MaxValue;
            }
        }
        static readonly float[] seamCeil = new float[3];

        /// <summary>Every painted gore quad the tile's BuildGores computed, in
        /// world space, whichever tile draws it (A, B, C, D as Bucket.Up
        /// takes them: triangles ABC and ACD).</summary>
        static readonly List<(Vector3 a, Vector3 b, Vector3 c, Vector3 d)> goreQuads = new List<(Vector3, Vector3, Vector3, Vector3)>(64);
        /// <summary>The quads of <see cref="goreQuads"/> one branch painted
        /// (from, to) and the plan box round them (min x, min z, max x, max z).</summary>
        static readonly List<(int from, int to, Vector4 box)> goreGroups = new List<(int, int, Vector4)>(16);

        static void StripQuad(CityMap map, TileMeshes tm, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            if ((b - a).sqrMagnitude < 1e-6f && (c - d).sqrMagnitude < 1e-6f) return;
            if (groundLog != null) { groundLog.Add((groundTag, a, b, c)); groundLog.Add((groundTag, a, c, d)); }
            var cen = (a + b + c + d) * 0.25f;
            bool paved = PavedAt(map, cen.x, cen.z);
            var o = tm.origin;
            GroundBucket(paved).Up(a - o, b - o, c - o, d - o,
                GroundUV(paved, a.x, a.z), GroundUV(paved, b.x, b.z), GroundUV(paved, c.x, c.z), GroundUV(paved, d.x, d.z));
        }

        /// <summary>A verge along any drawn line that is not a ribbon side:
        /// a junction fan's open chord, a gore nose on the ground.</summary>
        static void EmitVergeLine(CityMap map, Trims trims, TileMeshes tm, Vector3 a, Vector3 b, Vector2 outA, Vector2 outB, float shoulder)
        {
            EmitStrip(map, trims, tm, a, b, outA, outB, new StripShape { shoulder = shoulder, maxRun = VergeMaxRunM, noFins = true });
            tm.vergeMetres += Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        }

        /// <summary>Close the end of the solid ground behind a retaining run:
        /// a face from the strip's cross-section down into the lattice, so
        /// the shelf the wall hides cannot be driven into from its end.</summary>
        static void EmitSeal(CityMap map, Trims trims, TileMeshes tm, Vector3 edge, Vector2 outw, StripShape sh, Vector2 facing)
        {
            var o = tm.origin;
            var pW = new Vector2(edge.x + o.x, edge.z + o.z);
            GatherNear(map, pW, pW, sh.start + sh.maxRun + RoadsideRules.ToeTuckRunM);
            if (!SolveStrip(map, trims, pW, edge.y, outw, sh, profCur)) return;
            var bk = GroundBucket(false);
            for (int q = 0; q < 3; q++)
            {
                var a = profCur[q]; var b = profCur[q + 1];
                if ((b - a).sqrMagnitude < 1e-6f) continue;
                float ya = LatticeY(map, a.x, a.z) - RoadsideRules.ToeTuckM, yb = LatticeY(map, b.x, b.z) - RoadsideRules.ToeTuckM;
                if (ya >= a.y && yb >= b.y) continue;
                var aL = a - o; var bL = b - o;
                bk.Face(new Vector3(aL.x, Mathf.Min(ya, a.y), aL.z), aL, bL, new Vector3(bL.x, Mathf.Min(yb, b.y), bL.z), Flat(facing),
                        GroundUV(false, a.x, a.z), GroundUV(false, a.x, a.z), GroundUV(false, b.x, b.z), GroundUV(false, b.x, b.z));
            }
        }

        static readonly HashSet<int> nearSet = new HashSet<int>();
        static readonly List<int> nearSegs = new List<int>(128);

        /// <summary>The road segments a strip along a..b could meet.</summary>
        static void GatherNear(CityMap map, Vector2 a, Vector2 b, float reach)
        {
            nearSet.Clear(); nearSegs.Clear();
            float r = reach + CityElevation.MaxCorridorHalf;
            map.EdgeSegsInRect(Vector2.Min(a, b) - Vector2.one * r, Vector2.Max(a, b) + Vector2.one * r, nearSet);
            nearSegs.AddRange(nearSet);
            nearVersion++;
        }

        /// <summary>
        /// How far out from <paramref name="p"/> along <paramref name="outw"/>
        /// the ground is free of every other road's PAVEMENT AS DRAWN — the
        /// ribbons of the edges GatherNear found (<see cref="OutlineOf"/>)
        /// and the fans of their junctions — and that pavement's height where
        /// it is met. Pavement the ray STARTS on and is leaving is not met:
        /// our own ribbon, the road we continue through a node, the host a
        /// wedge lies on. A surface more than half a metre above is a deck
        /// the ground passes under.
        ///
        /// It used to test each road's nominal band — a slab per segment and a
        /// disc per vertex at the full half width, over the whole edge — and
        /// every road drawn short of that refused the verges beside it: an arm
        /// in the length its junction trims (the fan is the pavement there;
        /// South Mint Street's three arms left holes to the lattice between
        /// them), a road overlapped where two carriageways leave one node
        /// (East 3rd Street's slot, a 35 cm lip), the dead end of Duls Lane
        /// drawn into Dalton Avenue; and a strip that did stop at an arm's
        /// trimmed length read the arm's height, not the fan's (a 90 cm tuck
        /// on North Sharon Amity Road).
        /// </summary>
        static float ClearRun(CityMap map, Trims trims, Vector2 p, Vector2 outw, float y, float maxRun, out float otherY)
            => ClearRun(map, trims, p, outw, y, maxRun, out otherY, out _, out _);

        /// <summary>The ribbon that stopped the last ClearRun (-1: nothing,
        /// or a junction fan).</summary>
        static int clearRunBlockEdge = -1;
        /// <summary>The ribbon the last SolveStrip's connector tucked under
        /// (-1: none), for LayTucked.</summary>
        static int stripTuckEdge = -1;

        /// <param name="edgeRun">How far along the ray the blocking pavement's
        /// edge is (the run stops a VergeClearPadM short of it); NaN when
        /// nothing blocks.</param>
        /// <param name="intoFan">The blocking pavement is a junction's fan,
        /// whose triangles neither follow an arm's edge nor its height.</param>
        /// <param name="overhead">How far above <paramref name="y"/> a
        /// pavement is a deck the ground passes under rather than a road it
        /// meets. Half a metre for a verge; a squeeze's half strip passes
        /// the squeeze's own height band (see SolveStrip).</param>
        static float ClearRun(CityMap map, Trims trims, Vector2 p, Vector2 outw, float y, float maxRun, out float otherY, out float edgeRun, out bool intoFan,
                              float overhead = DeckOverheadM)
        {
            float best = maxRun, oyBest = float.NaN, erBest = float.NaN;
            bool fanBest = false;
            int blkBest = -1;
            GatherPavement(map, trims);
            float reach = maxRun + VergeClearPadM;
            var far = p + outw * reach;
            float bx0 = Mathf.Min(p.x, far.x), bx1 = Mathf.Max(p.x, far.x);
            float bz0 = Mathf.Min(p.y, far.y), bz1 = Mathf.Max(p.y, far.y);
            for (int k = 0; k < nearEdges.Count; k++)
            {
                var box = nearEdgeBox[k];
                if (bx1 < box.x || bx0 > box.z || bz1 < box.y || bz0 > box.w) continue;
                int oi = nearEdges[k];
                var ol = OutlineOf(map, trims, oi);
                if (ol.L == null || bx1 < ol.minX || bx0 > ol.maxX || bz1 < ol.minZ || bz0 > ol.maxZ) continue;
                paveSpans.Clear();
                for (int i = 1; i < ol.L.Length; i++)
                {
                    if (ol.BlockMisses(i, bx0, bz0, bx1, bz1)) { i += Outline.Block - 1; continue; }
                    Vector3 aL = ol.L[i - 1], bL = ol.L[i], bR = ol.R[i], aR = ol.R[i - 1];
                    if (bx1 < Mathf.Min(Mathf.Min(aL.x, bL.x), Mathf.Min(bR.x, aR.x)) || bx0 > Mathf.Max(Mathf.Max(aL.x, bL.x), Mathf.Max(bR.x, aR.x)) ||
                        bz1 < Mathf.Min(Mathf.Min(aL.z, bL.z), Mathf.Min(bR.z, aR.z)) || bz0 > Mathf.Max(Mathf.Max(aL.z, bL.z), Mathf.Max(bR.z, aR.z))) continue;
                    // the span as the ribbon draws it, its two triangles; only
                    // the SIDES are inset, so a point on a diagonal or on the
                    // cross-section between two spans is inside one or the other
                    if (TriInterval(aL, bL, bR, PavedInsetM, 0f, 0f, p, outw, reach, out float t0, out float t1) && t1 - t0 > 1e-3f)
                        paveSpans.Add((t0, t1, i * 2));
                    if (TriInterval(aL, bR, aR, 0f, PavedInsetM, 0f, p, outw, reach, out t0, out t1) && t1 - t0 > 1e-3f)
                        paveSpans.Add((t0, t1, i * 2 + 1));
                }
                int n = paveSpans.Count;
                if (n == 0) continue;
                SortSpans(paveSpans);
                // The run of overlapping intervals from t = 0. A strip starting
                // on this road's edge leaves it at once and does not meet it.
                float from = 0f;
                int j = 0;
                if (paveSpans[0].t0 <= 1e-3f)
                {
                    from = paveSpans[0].t1;
                    for (j = 1; j < n && paveSpans[j].t0 <= from + 1e-3f; j++) from = Mathf.Max(from, paveSpans[j].t1);
                    if (from > VertexSlackM)
                    {
                        // it starts on this road's pavement
                        float yIn = SpanHeight(ol, paveSpans[0].tri, p + outw * 1e-3f);
                        var o = map.edges[oi];
                        CityElevation.ProjectOn(o, p, out float sOn);
                        var tn = o.TangentAt(sOn);
                        var rt = new Vector2(-tn.y, tn.x);
                        float h0 = Vector2.Dot(p - o.PointAt(sOn), rt), hd = Vector2.Dot(outw, rt);
                        if (h0 * hd >= 0f || Mathf.Abs(hd) < 0.05f)
                        {
                            // Leaving it, or running along its edge (a nose): not
                            // met — unless we start deep inside a LOWER road's
                            // lanes, where the strip would lie over the rest of
                            // them. Two roads drawn a metre into each other (the
                            // Independence Expressway over Albemarle Road, 0.9 m
                            // up) grew the upper road's verge across the lower
                            // one's outside lane.
                            if (yIn < y - 0.1f && best > 0f && InsideDeep(ol, p)) { best = 0f; oyBest = yIn; erBest = 0f; fanBest = false; blkBest = oi; }
                        }
                        else if (yIn <= y + overhead)
                        {
                            // heading in: met where it stands
                            if (best > 0f) { best = 0f; oyBest = yIn; erBest = 0f; fanBest = false; blkBest = oi; }
                            continue;
                        }
                    }
                }
                for (; j < n; j++)
                {
                    var sp = paveSpans[j];
                    if (sp.t1 <= from + 1e-3f) continue;
                    float te = Mathf.Max(sp.t0, from);
                    if (te >= best + VergeClearPadM) break;
                    float fy = SpanHeight(ol, sp.tri, p + outw * (te + 1e-3f));
                    if (fy > y + overhead) continue;    // a deck the ground passes under
                    // stopped VergeClearPadM short of the pavement, or at it
                    // where the ray starts inside that pad
                    float run = te <= VergeClearPadM ? te : te - VergeClearPadM;
                    if (run < best) { best = run; oyBest = fy; erBest = te; fanBest = false; blkBest = oi; }
                    break;
                }
            }
            // the junction fans themselves, triangle by triangle
            foreach (int n in nearFans)
            {
                var fan = fanPolys[n];
                // nothing of it within the run
                float along = Mathf.Clamp(Vector2.Dot(fan.centre - p, outw), 0f, best);
                if ((p + outw * along - fan.centre).sqrMagnitude > (fan.reach + VergeClearPadM) * (fan.reach + VergeClearPadM)) continue;
                if (!FanEntry(fan, p, outw, best + VergeClearPadM, y + overhead, out float tIn, out float fy)) continue;
                // the pad as a ribbon's
                float run = tIn <= VergeClearPadM ? tIn : tIn - VergeClearPadM;
                if (run >= best) continue;
                best = run; oyBest = fy; erBest = tIn; fanBest = true; blkBest = -1;
            }
            otherY = oyBest; edgeRun = erBest; intoFan = fanBest;
            clearRunBlockEdge = blkBest;
            return best;
        }

        /// <summary>A junction fan's pavement in world space: the triangles
        /// BuildJunctions draws (<see cref="FanCorners"/>,
        /// <see cref="FanTriangles"/>), three points each, and the farthest
        /// one's plan distance from the node.</summary>
        struct FanPoly { public Vector2 centre; public Vector3[] tris; public float reach; }
        static readonly Dictionary<int, FanPoly> fanPolys = new Dictionary<int, FanPoly>();
        static readonly List<int> nearFans = new List<int>(16);
        static readonly HashSet<int> nearFanSet = new HashSet<int>();
        static readonly List<FanCorner> fanCornerScratch = new List<FanCorner>(12);
        static readonly List<int> fanTriIndex = new List<int>(48);
        /// <summary>GatherNear's count, and the one GatherPavement last read
        /// the pavement for.</summary>
        static int nearVersion, pavementVersion = -1;

        /// <summary>The roads (their drawn outlines, lazily) and patched nodes
        /// (their fans) of every segment GatherNear found, each built once per
        /// tile build.</summary>
        static void GatherPavement(CityMap map, Trims trims)
        {
            if (pavementVersion == nearVersion) return;
            pavementVersion = nearVersion;
            nearFans.Clear(); nearFanSet.Clear();
            nearEdges.Clear(); nearEdgeAt.Clear(); nearEdgeBox.Clear();
            foreach (int packed in nearSegs)
            {
                var o = map.edges[packed >> 12];
                // a plan box round the segments found, padded by the most a
                // drawn vertex strays from them (a collapsed clip's is clamped
                // six metres past the half width): no outline is built for a
                // road the strip cannot reach
                Vector2 a = o.pts[packed & 0xFFF], b = o.pts[(packed & 0xFFF) + 1];
                float pad = o.HalfMax + 7f;
                var box = new Vector4(Mathf.Min(a.x, b.x) - pad, Mathf.Min(a.y, b.y) - pad, Mathf.Max(a.x, b.x) + pad, Mathf.Max(a.y, b.y) + pad);
                if (nearEdgeAt.TryGetValue(o.index, out int k))
                {
                    var bb = nearEdgeBox[k];
                    nearEdgeBox[k] = new Vector4(Mathf.Min(bb.x, box.x), Mathf.Min(bb.y, box.y), Mathf.Max(bb.z, box.z), Mathf.Max(bb.w, box.w));
                    continue;
                }
                nearEdgeAt[o.index] = nearEdges.Count;
                nearEdges.Add(o.index);
                nearEdgeBox.Add(box);
                for (int end = 0; end < 2; end++)
                {
                    int n = end == 0 ? o.a : o.b;
                    if (!trims.patch[n]) continue;
                    n = FanKey(trims, n);   // a cluster's one paved area, once (plan A11)
                    if (!nearFanSet.Add(n)) continue;
                    var fan = FanPolyOf(map, trims, n);
                    if (fan.tris != null && fan.tris.Length >= 3) nearFans.Add(n);
                }
            }
        }

        /// <summary>A patched node's fan pavement (<see cref="FanPoly"/>),
        /// built once per tile build; its tris are null where it has fewer
        /// than three corners.</summary>
        static FanPoly FanPolyOf(CityMap map, Trims trims, int n)
        {
            if (fanPolys.TryGetValue(n, out var fan)) return fan;
            // scratch lists of its own: BuildJunctions is walking its own
            FanCorners(map, trims, n, Vector3.zero, fanCornerScratch);
            FanCentre(map, trims, n, out var np, out float npY);
            fan = new FanPoly { centre = np };
            if (fanCornerScratch.Count >= 3)
            {
                FanTriangles(fanCornerScratch, np, fanTriIndex);
                FanSteiner(map, trims, n, Vector3.zero, fanPolySteiner);
                RefineFan(fanCornerScratch, np, fanPolySteiner, fanTriIndex);
                var centre = new Vector3(np.x, npY + FanProudM, np.y);
                fan.tris = new Vector3[fanTriIndex.Count];
                for (int i = 0; i < fanTriIndex.Count; i++)
                {
                    int c = fanTriIndex[i];
                    fan.tris[i] = FanVertex(c, centre, fanCornerScratch, fanPolySteiner);
                    fan.reach = Mathf.Max(fan.reach, Vector2.Distance(np, new Vector2(fan.tris[i].x, fan.tris[i].z)));
                }
            }
            fanPolys[n] = fan;
            return fan;
        }

        /// <summary>A ribbon as the tiles draw it: every cross-section's two
        /// edge vertices in world space, from the same BuildSections the tile
        /// runs — tapers, squeezes, clips, mitres and all — whether they are
        /// on structure, and the plan box round them.</summary>
        sealed class Outline
        {
            public Vector3[] L, R;
            public bool[] elev;
            /// <summary>Span i (from section i - 1 to i) on structure by its
            /// middle: the tile's own rule for a deck span (plan L3).</summary>
            public bool[] deck;
            public float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            /// <summary>A plan box per block of <see cref="Block"/> spans (4
            /// floats: min x, min z, max x, max z, padded by BlockPadM), so a
            /// scan over a long outline skips the blocks nowhere near its
            /// query. Every scan's own per-span test still decides; a skipped
            /// block is one no span of which could pass it. WP-11's arcs gave
            /// a long freeway outline hundreds of spans, each tested by every
            /// verge probe in the tile (review 2026-09-30: tile p95 +50%).</summary>
            public float[] bb;
            public const int Block = 16;
            const float BlockPadM = 0.01f;
            public void BuildBlocks()
            {
                int nq = L.Length - 1, nb = (nq + Block - 1) / Block;
                bb = new float[nb * 4];
                for (int b = 0; b < nb; b++)
                {
                    float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
                    for (int j = b * Block, end = Mathf.Min(L.Length - 1, b * Block + Block); j <= end; j++)
                    {
                        x0 = Mathf.Min(x0, Mathf.Min(L[j].x, R[j].x)); x1 = Mathf.Max(x1, Mathf.Max(L[j].x, R[j].x));
                        z0 = Mathf.Min(z0, Mathf.Min(L[j].z, R[j].z)); z1 = Mathf.Max(z1, Mathf.Max(L[j].z, R[j].z));
                    }
                    bb[b * 4] = x0 - BlockPadM; bb[b * 4 + 1] = z0 - BlockPadM; bb[b * 4 + 2] = x1 + BlockPadM; bb[b * 4 + 3] = z1 + BlockPadM;
                }
            }
            /// <summary>At span i opening a block ((i - 1) % Block == 0): does
            /// that block's box miss the query box? The caller then steps
            /// past the whole block.</summary>
            public bool BlockMisses(int i, float x0, float z0, float x1, float z1)
            {
                if (((i - 1) & (Block - 1)) != 0 || bb == null) return false;
                int b = ((i - 1) / Block) * 4;
                return x1 < bb[b] || x0 > bb[b + 2] || z1 < bb[b + 1] || z0 > bb[b + 3];
            }
        }
        static readonly Dictionary<int, Outline> outlines = new Dictionary<int, Outline>();
        static readonly List<int> nearEdges = new List<int>(32);
        /// <summary>Per near edge: min x, min z, max x, max z of the segments
        /// GatherNear found on it, padded.</summary>
        static readonly List<Vector4> nearEdgeBox = new List<Vector4>(32);
        static readonly Dictionary<int, int> nearEdgeAt = new Dictionary<int, int>();
        static readonly List<Section> outlineSections = new List<Section>(64);
        static readonly List<float> outlineEnds = new List<float>(8);
        static readonly TileMeshes worldOrigin = new TileMeshes { origin = Vector3.zero };
        static readonly List<(float t0, float t1, int tri)> paveSpans = new List<(float, float, int)>(32);
        /// <summary>How far inside a ribbon's drawn side a point must be to
        /// stand on its pavement: a strip starts ON its own edge.</summary>
        const float PavedInsetM = 0.02f;
        /// <summary>How far inside a lower road's drawn sides a strip must
        /// start before it is refused for lying over its lanes.</summary>
        const float DeepInsetM = 0.3f;

        static Outline OutlineOf(CityMap map, Trims trims, int ei)
        {
            if (outlines.TryGetValue(ei, out var ol)) return ol;
            ol = new Outline();
            outlines[ei] = ol;
            var e = map.edges[ei];
            float sMin = trims.atA[ei], sMax = e.length - trims.atB[ei];
            if (sMax - sMin < 0.6f || (e.a == e.b && e.length < 1f)) return ol;
            // BuildSections rebuilds the section list and the structure ends
            // a caller may be walking (DecideSideFlags, EmitSide)
            outlineSections.Clear(); outlineSections.AddRange(sections);
            outlineEnds.Clear(); outlineEnds.AddRange(endScratch);
            BuildSections(map, trims, worldOrigin, e, sMin, sMax);
            int n = sections.Count;
            if (n >= 2)
            {
                ol.L = new Vector3[n]; ol.R = new Vector3[n]; ol.elev = new bool[n]; ol.deck = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    ol.L[i] = sections[i].L; ol.R[i] = sections[i].R; ol.elev[i] = sections[i].elev;
                    if (i > 0) ol.deck[i] = e.ElevatedAt(0.5f * (sections[i - 1].s + sections[i].s));
                    ol.minX = Mathf.Min(ol.minX, Mathf.Min(ol.L[i].x, ol.R[i].x)); ol.maxX = Mathf.Max(ol.maxX, Mathf.Max(ol.L[i].x, ol.R[i].x));
                    ol.minZ = Mathf.Min(ol.minZ, Mathf.Min(ol.L[i].z, ol.R[i].z)); ol.maxZ = Mathf.Max(ol.maxZ, Mathf.Max(ol.L[i].z, ol.R[i].z));
                }
                ol.BuildBlocks();
            }
            sections.Clear(); sections.AddRange(outlineSections);
            endScratch.Clear(); endScratch.AddRange(outlineEnds);
            return ol;
        }

        /// <summary>A ribbon's outline without MeetPavement's step
        /// (<see cref="RawSectionsOf"/>), for MeetPavement to read, so which
        /// of two touching roads is sectioned first changes neither.</summary>
        static Outline RawOutlineOf(CityMap map, Trims trims, int ei)
        {
            if (rawOutlines.TryGetValue(ei, out var ol)) return ol;
            ol = new Outline();
            rawOutlines[ei] = ol;
            var e = map.edges[ei];
            float sMin = trims.atA[ei], sMax = e.length - trims.atB[ei];
            if (sMax - sMin < 0.6f || (e.a == e.b && e.length < 1f)) return ol;
            var raw = RawSectionsOf(map, trims, e, sMin, sMax);
            int n = raw.Count;
            if (n >= 2)
            {
                ol.L = new Vector3[n]; ol.R = new Vector3[n]; ol.elev = new bool[n]; ol.deck = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    ol.L[i] = raw[i].L; ol.R[i] = raw[i].R; ol.elev[i] = raw[i].elev;
                    if (i > 0) ol.deck[i] = e.ElevatedAt(0.5f * (raw[i - 1].s + raw[i].s));
                    ol.minX = Mathf.Min(ol.minX, Mathf.Min(ol.L[i].x, ol.R[i].x)); ol.maxX = Mathf.Max(ol.maxX, Mathf.Max(ol.L[i].x, ol.R[i].x));
                    ol.minZ = Mathf.Min(ol.minZ, Mathf.Min(ol.L[i].z, ol.R[i].z)); ol.maxZ = Mathf.Max(ol.maxZ, Mathf.Max(ol.L[i].z, ol.R[i].z));
                }
                ol.BuildBlocks();
            }
            return ol;
        }
        static readonly Dictionary<int, Outline> rawOutlines = new Dictionary<int, Outline>();

        /// <summary>The height of one of an outline's span triangles
        /// (span * 2, + 1 for the second) over a plan point.</summary>
        static float SpanHeight(Outline ol, int tri, Vector2 q)
        {
            int i = tri >> 1;
            return (tri & 1) == 0 ? TriHeight(ol.L[i - 1], ol.L[i], ol.R[i], q)
                                  : TriHeight(ol.L[i - 1], ol.R[i], ol.R[i - 1], q);
        }

        /// <summary>For the roadside probe: <see cref="OutlineHeightAt"/> of
        /// an edge's ribbon as the strips read it.</summary>
        public static float OutlineHeight(CityMap map, Trims trims, int ei, float x, float z)
            => OutlineHeightAt(OutlineOf(map, trims, ei), new Vector2(x, z));

        /// <summary>A ribbon's drawn surface height over a plan point, the
        /// lowest where two of its spans overlap there; NaN off its pavement.</summary>
        static float OutlineHeightAt(Outline ol, Vector2 q)
        {
            float h = float.NaN;
            if (ol.L == null || q.x < ol.minX || q.x > ol.maxX || q.y < ol.minZ || q.y > ol.maxZ) return h;
            for (int i = 1; i < ol.L.Length; i++)
            {
                if (ol.BlockMisses(i, q.x, q.y, q.x, q.y)) { i += Outline.Block - 1; continue; }
                for (int k = 0; k < 2; k++)
                {
                    Vector3 a = ol.L[i - 1], b = k == 0 ? ol.L[i] : ol.R[i], c = k == 0 ? ol.R[i] : ol.R[i - 1];
                    if (!TriInterval(a, b, c, q, Vector2.right, 0f, out _, out _)) continue;
                    float t = TriHeight(a, b, c, q);
                    if (float.IsNaN(h) || t < h) h = t;
                }
            }
            return h;
        }

        /// <summary>Is a point on a ribbon's pavement at least
        /// <see cref="DeepInsetM"/> in from its drawn sides?</summary>
        static bool InsideDeep(Outline ol, Vector2 q)
        {
            for (int i = 1; i < ol.L.Length; i++)
            {
                if (ol.BlockMisses(i, q.x, q.y, q.x, q.y)) { i += Outline.Block - 1; continue; }
                if (TriInterval(ol.L[i - 1], ol.L[i], ol.R[i], DeepInsetM, 0f, 0f, q, Vector2.right, 0f, out _, out _)) return true;
                if (TriInterval(ol.L[i - 1], ol.R[i], ol.R[i - 1], 0f, DeepInsetM, 0f, q, Vector2.right, 0f, out _, out _)) return true;
            }
            return false;
        }

        /// <summary>
        /// Where a ray first ENTERS a fan's pavement within
        /// <paramref name="limit"/>, and the fan's surface height there. The
        /// run of overlapping triangle intervals the ray starts in is left at
        /// once from a corner or chord on the fan's own edge, and not met; a
        /// strip starting any deeper inside is on the fan's pavement already,
        /// and met where it stands (two junctions 7 m apart on East Morehead
        /// Street draw their fans over each other, and one's chord verge
        /// crossed the other's). Any part of it standing higher than
        /// <paramref name="yMax"/> is a deck the ground passes under, as a
        /// ribbon's span is in ClearRun: the ray looks on past it for a lower
        /// part, instead of passing the whole fan by.
        /// </summary>
        static bool FanEntry(FanPoly fan, Vector2 p, Vector2 dir, float limit, float yMax, out float tIn, out float y)
        {
            tIn = limit; y = float.NaN;
            var T = fan.tris;
            fanSpans.Clear();
            for (int i = 0; i + 2 < T.Length; i += 3)
            {
                // The fan's own edge is inset as a ribbon's sides are, and
                // only that edge: a centre triangle's far side, corner to
                // corner (the sides from the node are shared with the next
                // triangle). A verge running out from a corner ALONG a chord
                // was inside the closed triangle all the way, "on the fan
                // already", and collapsed onto the corner; the wedge between
                // it and the next cross-section stood 0.7 m open beside the
                // chord's retaining face (e5252 at node 6824).
                float inBC = T[i].x == fan.centre.x && T[i].z == fan.centre.y ? PavedInsetM : 0f;
                if (TriInterval(T[i], T[i + 1], T[i + 2], 0f, inBC, 0f, p, dir, limit, out float t0, out float t1) && t1 - t0 > 1e-3f)
                    fanSpans.Add((t0, t1, i));
            }
            if (fanSpans.Count == 0) return false;
            SortSpans(fanSpans);
            float from = 0f;
            int j = 0;
            if (fanSpans[0].t0 <= 1e-3f)
            {
                from = fanSpans[0].t1;
                for (j = 1; j < fanSpans.Count && fanSpans[j].t0 <= from + 1e-3f; j++) from = Mathf.Max(from, fanSpans[j].t1);
                int i0 = fanSpans[0].tri;
                float yStart = TriHeight(T[i0], T[i0 + 1], T[i0 + 2], p + dir * 1e-3f);
                if (from > VertexSlackM && yStart <= yMax) { tIn = 0f; y = yStart; return true; }
            }
            for (; j < fanSpans.Count; j++)
            {
                if (fanSpans[j].t1 <= from + 1e-3f) continue;
                float t = Mathf.Max(fanSpans[j].t0, from);
                if (t >= limit) break;
                int i = fanSpans[j].tri;
                float yt = TriHeight(T[i], T[i + 1], T[i + 2], p + dir * (t + 1e-3f));
                if (yt > yMax) continue;    // a part of it the ground passes under
                tIn = t; y = yt;
                return true;
            }
            return false;
        }
        static readonly List<(float t0, float t1, int tri)> fanSpans = new List<(float, float, int)>(12);

        /// <summary>Does a strip from <paramref name="p"/> along
        /// <paramref name="outw"/> start ON a junction fan's pavement (by
        /// <see cref="FanEntry"/>'s rule) whose surface there is no higher
        /// than <paramref name="yMax"/>? Reads the fans of the roads GatherNear
        /// last found.</summary>
        static bool StartsOnFan(CityMap map, Trims trims, Vector2 p, Vector2 outw, float run, float yMax)
        {
            GatherPavement(map, trims);
            foreach (int n in nearFans)
            {
                var fan = fanPolys[n];
                float r = fan.reach + run;
                if ((p - fan.centre).sqrMagnitude > r * r) continue;
                if (FanEntry(fan, p, outw, run, yMax, out float tIn, out _) && tIn <= 1e-3f) return true;
            }
            return false;
        }

        /// <summary>Order a ray's pavement intervals by where they start: an
        /// insertion sort, stable and allocation-free. ClearRun sorts a few
        /// intervals per road on every verge cross-section of every tile
        /// build, and List.Sort with a comparison wraps a new comparer on
        /// each call (Mono and IL2CPP): garbage on every tile the city
        /// streams in.</summary>
        static void SortSpans(List<(float t0, float t1, int tri)> spans)
        {
            for (int i = 1; i < spans.Count; i++)
            {
                var x = spans[i];
                int j = i - 1;
                for (; j >= 0 && spans[j].t0 > x.t0; j--) spans[j + 1] = spans[j];
                spans[j + 1] = x;
            }
        }

        /// <summary>The parameter interval a plan ray spends inside a
        /// triangle (any winding), clipped to [0, <paramref name="limit"/>]. A
        /// limit of 0 asks whether the point itself is inside.</summary>
        static bool TriInterval(Vector3 A, Vector3 B, Vector3 C, Vector2 p, Vector2 dir, float limit, out float t0, out float t1)
            => TriInterval(A, B, C, 0f, 0f, 0f, p, dir, limit, out t0, out t1);

        /// <summary>The same with each edge (AB, BC, CA) moved in by an inset
        /// (out, where negative).</summary>
        static bool TriInterval(Vector3 A, Vector3 B, Vector3 C, float inAB, float inBC, float inCA,
                                Vector2 p, Vector2 dir, float limit, out float t0, out float t1)
        {
            t0 = 0f; t1 = limit;
            float area = (B.x - A.x) * (C.z - A.z) - (C.x - A.x) * (B.z - A.z);
            if (Mathf.Abs(area) < 1e-4f) return false;
            float sgn = Mathf.Sign(area);
            for (int e = 0; e < 3; e++)
            {
                Vector3 P = e == 0 ? A : e == 1 ? B : C, Q = e == 0 ? B : e == 1 ? C : A;
                float inset = e == 0 ? inAB : e == 1 ? inBC : inCA;
                float ex = Q.x - P.x, ez = Q.z - P.z;
                // inside: sgn * cross(edge, point - P) >= inset * |edge|, linear in t
                float c0 = sgn * (ex * (p.y - P.z) - ez * (p.x - P.x)) - inset * Mathf.Sqrt(ex * ex + ez * ez);
                float c1 = sgn * (ex * dir.y - ez * dir.x);
                if (Mathf.Abs(c1) < 1e-9f) { if (c0 < 0f) return false; continue; }
                float t = -c0 / c1;
                if (c1 > 0f) t0 = Mathf.Max(t0, t); else t1 = Mathf.Min(t1, t);
                if (t0 > t1) return false;
            }
            return true;
        }

        /// <summary>The height of a triangle's plane over a plan point.</summary>
        static float TriHeight(Vector3 A, Vector3 B, Vector3 C, Vector2 q)
        {
            float det = (B.x - A.x) * (C.z - A.z) - (C.x - A.x) * (B.z - A.z);
            if (Mathf.Abs(det) < 1e-6f) return A.y;
            float u = ((q.x - A.x) * (C.z - A.z) - (C.x - A.x) * (q.y - A.z)) / det;
            float v = ((B.x - A.x) * (q.y - A.z) - (q.x - A.x) * (B.z - A.z)) / det;
            return A.y + u * (B.y - A.y) + v * (C.y - A.y);
        }

        /// <summary>Clip a ray parameter range to v0 + dv t in [lo, hi].</summary>
        static bool Slab(float v0, float dv, float lo, float hi, ref float tLo, ref float tHi)
        {
            if (Mathf.Abs(dv) < 1e-6f) return v0 >= lo && v0 <= hi && tLo <= tHi;
            float t1 = (lo - v0) / dv, t2 = (hi - v0) / dv;
            if (t1 > t2) { float t = t1; t1 = t2; t2 = t; }
            tLo = Mathf.Max(tLo, t1); tHi = Mathf.Min(tHi, t2);
            return tLo <= tHi;
        }

        /// <summary>
        /// A pier under a deck — unless it would stand in the road the deck
        /// crosses. A pier every 26 m landed in the carriageway below at one
        /// crossing in three; now it is nudged along the deck to the nearest
        /// clear spot, or left out.
        /// </summary>
        static readonly float[] PierNudges = { 0f, -5f, 5f, -10f, 10f, -14f, 14f };
        static void EmitPier(CityMap map, Trims trims, CityMap.Edge e, TileMeshes tm, float sAt)
        {
            foreach (var dS in PierNudges)
            {
                float s = sAt + dS;
                if (s < 2f || s > e.length - 2f) continue;
                // plan A2: never nudged in under a union (its owner's bents carry it)
                if (InUnionDeckRun(trims, e.index, s)) continue;
                var p = e.PointAt(s);
                float deckY = e.YAt(s) - CityElevation.DeckThick;
                var tan = e.TangentAt(s);
                float hw = Mathf.Max(0.7f, e.width * 0.18f);
                if (PierBlocked(map, e, p, tan, hw, deckY)) continue;
                float gy = CityElevation.GroundY(map, p.x, p.y);
                if (deckY - gy < 2.2f) return;

                var con = buckets[(int)Slot.Concrete];
                var right = new Vector3(-tan.y, 0f, tan.x);
                var fwd = new Vector3(tan.x, 0f, tan.y);
                var c = new Vector3(p.x - tm.origin.x, 0f, p.y - tm.origin.z);
                var bottom = c + Vector3.up * (gy - 0.6f);
                var top = c + Vector3.up * deckY;
                EmitColumn(con, bottom, top, right * hw, fwd * 0.7f);

                tm.solids.Add(new SolidBox
                {
                    center = c + Vector3.up * ((gy - 0.6f + deckY) * 0.5f),
                    size = new Vector3(hw * 2f, deckY - gy + 0.6f, 1.4f),
                    yawDeg = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg,
                });
                if (pierLog != null) RecordPier(e, sAt, s, p, gy - 0.6f, deckY, hw, tan);   // AuditView's record (audits only)
                return;
            }
        }

        static readonly HashSet<int> pierScratch = new HashSet<int>();
        /// <summary>Plan clearance a pier's column keeps from the paved edge
        /// of every road below its deck: outside the shoulder a car comes back
        /// onto the road over.</summary>
        const float PierClearM = 1.5f;

        /// <summary>Would a pier's column (its footprint: <paramref name="hw"/>
        /// across the deck either side of <paramref name="p"/>, 0.7 m along
        /// it) stand within <see cref="PierClearM"/> of the pavement of a road
        /// passing BELOW the deck? (A road at the deck's own height is the
        /// deck's neighbour, not something it crosses.) It asked of the column's
        /// CENTRE only, 1.3 m past the road's half width, while the column is
        /// up to 5.5 m wide across the deck: at an oblique crossing its corner
        /// stood a hand outside a street's edge, the one face the roadside
        /// audit found in the way of the body box coming back onto North
        /// Brevard, East 10th and East 4th Streets.</summary>
        static bool PierBlocked(CityMap map, CityMap.Edge deck, Vector2 p, Vector2 tan, float hw, float deckY)
        {
            pierScratch.Clear();
            float reach = hw + 30f;
            map.EdgeSegsInRect(p - Vector2.one * reach, p + Vector2.one * reach, pierScratch);
            var right = new Vector2(-tan.y, tan.x);
            foreach (var packed in pierScratch)
            {
                int oi = packed >> 12, si = packed & 0xFFF;
                if (oi == deck.index) continue;
                var o = map.edges[oi];
                Vector2 a = o.pts[si], d = o.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                float at = o.s[si] + Mathf.Sqrt(L2) * t;
                if (o.YAt(at) >= deckY - 1.2f) continue;
                float dist = SegRectDistance(a - p, o.pts[si + 1] - p, right, tan, hw, 0.7f);
                if (dist <= o.HalfMax + PierClearM) return true;
            }
            // nor in a junction's paved area below the deck (leftover item 3: a
            // crossing's cluster paves the ground between the roads under I-85)
            float rr = hw + PierClearM;
            foreach (var f in fanFloors)
            {
                if (f.ring == null || f.ring.Length < 3 || f.tris == null || f.tris.Length == 0) continue;
                if (p.x < f.x0 - rr || p.x > f.x1 + rr || p.y < f.z0 - rr || p.y > f.z1 + rr) continue;
                if (f.tris[0].y >= deckY - 1.2f) continue;   // a junction on the deck itself
                if (CityMap.PointInPoly(f.ring, p)) return true;
                for (int i = 0; i < f.ring.Length; i++)
                {
                    Vector2 q0 = f.ring[i], dq = f.ring[(i + 1) % f.ring.Length] - q0;
                    float L2 = dq.sqrMagnitude;
                    float u = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - q0, dq) / L2) : 0f;
                    if (Vector2.Distance(p, q0 + dq * u) < rr) return true;
                }
            }
            return false;
        }

        /// <summary>Plan distance between a segment (relative to a rectangle's
        /// centre) and the rectangle with unit axes <paramref name="ax"/>,
        /// <paramref name="ay"/> and half extents <paramref name="hx"/>,
        /// <paramref name="hy"/>; zero where they overlap.</summary>
        static float SegRectDistance(Vector2 a, Vector2 b, Vector2 ax, Vector2 ay, float hx, float hy)
        {
            var la = new Vector2(Vector2.Dot(a, ax), Vector2.Dot(a, ay));
            var lb = new Vector2(Vector2.Dot(b, ax), Vector2.Dot(b, ay));
            var dl = lb - la;
            float tLo = 0f, tHi = 1f;
            if (Slab(la.x, dl.x, -hx, hx, ref tLo, ref tHi) && Slab(la.y, dl.y, -hy, hy, ref tLo, ref tHi)) return 0f;
            float PointBox(Vector2 q) => new Vector2(Mathf.Max(Mathf.Abs(q.x) - hx, 0f), Mathf.Max(Mathf.Abs(q.y) - hy, 0f)).magnitude;
            float PointSeg(Vector2 q)
            {
                float L2 = dl.sqrMagnitude;
                float u = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(q - la, dl) / L2) : 0f;
                return Vector2.Distance(q, la + dl * u);
            }
            float best = Mathf.Min(PointBox(la), PointBox(lb));
            best = Mathf.Min(best, PointSeg(new Vector2(hx, hy)));
            best = Mathf.Min(best, PointSeg(new Vector2(-hx, hy)));
            best = Mathf.Min(best, PointSeg(new Vector2(hx, -hy)));
            best = Mathf.Min(best, PointSeg(new Vector2(-hx, -hy)));
            return best;
        }

        static void EmitColumn(Bucket bk, Vector3 bottom, Vector3 top, Vector3 half1, Vector3 half2)
        {
            var b1 = bottom + half1 + half2; var b2 = bottom + half1 - half2;
            var b3 = bottom - half1 - half2; var b4 = bottom - half1 + half2;
            var t1 = top + half1 + half2; var t2 = top + half1 - half2;
            var t3 = top - half1 - half2; var t4 = top - half1 + half2;
            float vh = (top.y - bottom.y) / 6f;
            bk.Quad(b1, t1, t2, b2, new Vector2(0.55f, 0), new Vector2(0.55f, vh), new Vector2(0.7f, vh), new Vector2(0.7f, 0));
            bk.Quad(b2, t2, t3, b3, new Vector2(0.55f, 0), new Vector2(0.55f, vh), new Vector2(0.7f, vh), new Vector2(0.7f, 0));
            bk.Quad(b3, t3, t4, b4, new Vector2(0.55f, 0), new Vector2(0.55f, vh), new Vector2(0.7f, vh), new Vector2(0.7f, 0));
            bk.Quad(b4, t4, t1, b1, new Vector2(0.55f, 0), new Vector2(0.55f, vh), new Vector2(0.7f, vh), new Vector2(0.7f, 0));
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// The fan at a junction node: one triangle per pair of adjacent
        /// arm corners, from the node centre. Corners sit at each arm's own
        /// trim AT THAT ARM'S HEIGHT — the fan is the surface between the
        /// arms, not a plate laid over them — and the centre at the node's.
        ///
        /// Its perimeter between arms (never across a road mouth) is what a
        /// ribbon's side is: a render-only face and a verge, with the corner
        /// between an arm's verge and the chord's filled — or, on a chord
        /// beside an arm that is on a deck at its trim or over a drop
        /// (<see cref="ChordRailed"/>), a fascia and a rail. A fan on
        /// STRUCTURE (a deck arm, or the node a metre over its ground) gets
        /// a soffit. Fans never read structure at all before, and 15 of them
        /// stood on decks with 42 open chords a car's width or more (21 m at
        /// West Boulevard and Fordham Road).
        /// </summary>
        static IEnumerable<int> BuildJunctions(CityMap map, Trims trims, TileMeshes tm,
                                   Vector2 min, Vector2 max)
        {
            var con = buckets[(int)Slot.Concrete];
            var corners = cornerScratch;
            var fanTris = fanTriScratch;

            segScratch.Clear();
            map.EdgeSegsInRect(min - Vector2.one * 4f, max + Vector2.one * 4f, segScratch);
            var nodesHere = new HashSet<int>();
            foreach (var packed in segScratch)
            {
                var e = map.edges[packed >> 12];
                nodesHere.Add(e.a); nodesHere.Add(e.b);
            }

            foreach (var n in nodesHere)
            {
                yield return 0;   // WP-09: a junction a step
                stepPhase = 3; stepItem = n; stepPart = 0;
                var np = map.nodes[n];
                if (!trims.patch[n])
                {
                    // roads pass L8 (A17): a plain dead end's verge across its end
                    if (BulbsOn && !(np.x < min.x || np.x >= max.x || np.y < min.y || np.y >= max.y)) EmitDeadEnd(map, trims, tm, n);
                    continue;
                }
                if (np.x < min.x || np.x >= max.x || np.y < min.y || np.y >= max.y) continue;
                // a junction cluster is drawn once, by its lowest member (plan A11)
                if (FanKey(trims, n) != n) continue;

                // A junction is paved with its MAIN road (leftover item 3: a
                // junction aged from its own node read as a patch at Trade x
                // Tryon); PSX_CITY_FANSURF=0 ages it from the node as before.
                int fanSlot = (int)SlotOf(JunctionProfile, FanSurface(map, trims, n));
                var bk = buckets[fanSlot];

                const float proud = FanProudM;
                FanCorners(map, trims, n, tm.origin, corners);
                if (corners.Count < 3) continue;

                FanCentre(map, trims, n, out var fc, out float fcY);
                var centre = new Vector3(fc.x - tm.origin.x, fcY + proud, fc.y - tm.origin.z);
                FanTriangles(corners, new Vector2(centre.x, centre.z), fanTris);
                // a cluster's members at their own heights (plan A11)
                FanSteiner(map, trims, n, tm.origin, fanSteinerScratch);
                RefineFan(corners, new Vector2(centre.x, centre.z), fanSteinerScratch, fanTris);
                int centerI = bk.v.Count;
                // roads pass L8 (plan A13): the through road's lines across the mouth
                bool through = ThroughLines(map, trims, n, tm.origin, false);
                int fanTapAt = -1;
                if (tm.tap != null)
                {
                    ulong mouths = 0;
                    for (int i = 0; i < corners.Count && i < 64; i++) if (corners[i].mouthNext) mouths |= 1UL << i;
                    fanTapAt = tm.tap.fans.Count;
                    tm.tap.fans.Add(new RoadTap.Fan { slot = fanSlot,
                                                      bucketV = centerI, count = corners.Count + 1 + fanSteinerScratch.Count, node = n, mouths = mouths, triStart = bk.t.Count, triCount = fanTris.Count / 3 });
                }
                bk.v.Add(centre);
                bk.uv.Add(new Vector2(fc.x / 12f, fc.y / 12f));
                for (int i = 0; i < corners.Count; i++)
                {
                    bk.v.Add(corners[i].pos);
                    bk.uv.Add(new Vector2((corners[i].pos.x + tm.origin.x) / 12f,
                                          (corners[i].pos.z + tm.origin.z) / 12f));
                }
                foreach (var sp in fanSteinerScratch)
                {
                    bk.v.Add(sp);
                    bk.uv.Add(new Vector2((sp.x + tm.origin.x) / 12f, (sp.z + tm.origin.z) / 12f));
                }
                // anticlockwise in map view is clockwise seen from above: up
                if (through)
                {
                    EmitCutFan(tm, n, bk, corners, centre, fanSteinerScratch, fanTris, out int cutT0, out int cutTN);
                    if (fanTapAt >= 0)
                    {
                        var f = tm.tap.fans[fanTapAt];
                        f.count = bk.v.Count - centerI; f.triStart = cutT0; f.triCount = cutTN;
                        tm.tap.fans[fanTapAt] = f;
                    }
                    ReleaseStrips();
                }
                else
                    for (int i = 0; i + 2 < fanTris.Count; i += 3)
                    {
                        bk.t.Add(centerI + fanTris[i]); bk.t.Add(centerI + fanTris[i + 2]); bk.t.Add(centerI + fanTris[i + 1]);
                    }
                tm.patchCount++;

                bool onStructure = FanOnStructure(map, trims, n);
                float dk = CityElevation.DeckThick;
                if (onStructure)
                {
                    // the soffit: the fan again, a deck's thickness down, facing down
                    var uvS = new Vector2(0.5f, 0.5f);
                    Vector3 At(int k) => FanVertex(k, centre, corners, fanSteinerScratch) + Vector3.down * dk;
                    for (int i = 0; i + 2 < fanTris.Count; i += 3)   // Tri emits (a, c, b): anticlockwise from above, down
                        con.Tri(At(fanTris[i]), At(fanTris[i + 2]), At(fanTris[i + 1]), uvS, uvS, uvS);
                }

                // The perimeter, minus the road mouths. The two corners of
                // one arm are adjacent, so an arm's own pair IS its mouth, and
                // the envelope of two overlapping mouths is flagged like one;
                // the rest face open ground.
                for (int i = 0; i < corners.Count; i++)
                {
                    var k0 = corners[i];
                    var k1 = corners[(i + 1) % corners.Count];
                    if (k0.mouthNext) continue;                 // road mouth
                    var chord = new Vector2(k1.pos.x - k0.pos.x, k1.pos.z - k0.pos.z);
                    float len = chord.magnitude;
                    if (len < 0.05f) continue;
                    // the perimeter runs anticlockwise: outward is its right
                    var nrm = new Vector2(chord.y, -chord.x) / len;
                    if (ChordRailed(map, trims, n, k0, k1, tm.origin))
                    {
                        // A deck edge: a fascia a deck deep. A grounded fan's
                        // chord over a drop: a retaining face down to the land.
                        float y0 = k0.pos.y - dk, y1 = k1.pos.y - dk;
                        if (!onStructure)
                        {
                            y0 = Mathf.Min(k0.pos.y - KerbFaceM, LatticeY(map, k0.pos.x + tm.origin.x + nrm.x * RailOverhangM, k0.pos.z + tm.origin.z + nrm.y * RailOverhangM) - CityElevation.CorridorSink);
                            y1 = Mathf.Min(k1.pos.y - KerbFaceM, LatticeY(map, k1.pos.x + tm.origin.x + nrm.x * RailOverhangM, k1.pos.z + tm.origin.z + nrm.y * RailOverhangM) - CityElevation.CorridorSink);
                        }
                        (onStructure ? con : barrierBucket).WallSloped(k0.pos, k1.pos, y0, k0.pos.y, y1, k1.pos.y,
                                                                         nrm, 0f, 0.6f, 0f, 0.15f);
                        // only where it stands in no other road's lane (RailRunsOffLanes)
                        tm.railMetres += EmitRailOffLanes(map, trims, tm.origin, k0.pos, k1.pos, -nrm, -nrm, onStructure ? dk : RailBuryM, 0f, len / RoadVTile, n,
                                                          new RailRecord { edge = -1, side = 0, node = n });
                        continue;
                    }
                    kerbBucket.WallSloped(k0.pos, k1.pos, k0.pos.y - KerbFaceM, k0.pos.y, k1.pos.y - KerbFaceM, k1.pos.y,
                                          nrm, 0f, 0.6f, 0f, 0.05f);
                    if (groundLog != null) groundTag = $"fan chord verge node {n} e{k0.edge}-e{k1.edge}";
                    // a curb return's pieces share their cross-sections (plan A10)
                    EmitVergeLine(map, trims, tm, k0.pos, k1.pos, RingOut(corners, i, nrm, true), RingOut(corners, (i + 1) % corners.Count, nrm, false), VergeShoulderM);
                    if (groundLog != null) groundTag = $"corner fill node {n} arm e{k0.edge}";
                    if (!k0.arc) CornerFill(map, trims, tm, k0.node, k0.pos, k0.yArm, map.edges[k0.edge], k0.side, nrm);
                    if (groundLog != null) groundTag = $"corner fill node {n} arm e{k1.edge}";
                    if (!k1.arc) CornerFill(map, trims, tm, k1.node, k1.pos, k1.yArm, map.edges[k1.edge], k1.side, nrm);
                }
            }
        }

        /// <summary>A fan stands a hair above the arm ends, against z-fighting.</summary>
        const float FanProudM = 0.012f;

        struct FanCorner
        {
            public float ang; public Vector3 pos; public int edge, side; public float yArm;
            /// <summary>The perimeter from this corner to the next is no free
            /// edge: the arm's own mouth, a stretch of the envelope where two
            /// mouths overlap (<see cref="PokeEnvelope"/>), or the chord across
            /// a buried arm. No kerb, verge or rail stands on it.</summary>
            public bool mouthNext;
            /// <summary>Not a ribbon corner: a point the perimeter was carried
            /// out to along an arm's edge line, round the outside of a bend.
            /// <see cref="edge"/> and <see cref="side"/> name the arm whose
            /// edge line it lies on, so a verge corner there reads that arm.</summary>
            public bool extra;
            /// <summary>The node the arm <see cref="edge"/> meets this ring at
            /// (a cluster's ring holds several members' arms).</summary>
            public int node;
            /// <summary>A curb return's point (its two tangent points and the
            /// arc between): its verge meets the next piece's on their bisector.</summary>
            public bool arc;
        }
        static readonly List<FanCorner> cornerScratch = new List<FanCorner>(12);

        struct FanArm
        {
            public int edge, lateSide, node;
            public float ang, trim, hw, yArm, yNode;
            public Vector2 outDir, early, late;
            /// <summary>A buried arm was dropped between the previous arm and
            /// this one: its ribbon (or the next fan along it) lies beyond
            /// the chord between them, across the angle from its early corner
            /// to its late one.</summary>
            public bool afterBuried;
            public Vector2 buriedEarly, buriedLate;
        }
        static readonly List<FanArm> fanArmScratch = new List<FanArm>(8);

        static float Cross2(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        /// <summary>Where the ray from <paramref name="c"/> through
        /// <paramref name="q"/> crosses the chord a-b, as a fraction along it
        /// (clamped; 0 where the two are parallel).</summary>
        static float ChordOnRay(Vector2 a, Vector2 b, Vector2 c, Vector2 q)
        {
            float den = Cross2(b - a, q - c);
            if (Mathf.Abs(den) < 1e-5f) return 0f;
            return Mathf.Clamp01(Cross2(c - a, q - c) / den);
        }

        /// <summary>Is this arm a branch clipped against its host at the
        /// node, drawn no wider than a sliver at its trim? (The clip table is
        /// the tile's, and every tile within reach of the node holds it.)</summary>
        static bool ArmCollapsedAtTrim(CityMap map, Trims trims, CityMap.Edge e, int node)
        {
            if (trims.BranchAt(e, node) < 0) return false;
            float trim = trims.TrimAt(e, node);
            LaneExtents(map, trims, e, e.a == node ? trim : e.length - trim, out float hwL, out float hwR);
            return hwL + hwR < 0.3f;
        }

        /// <summary>Is <paramref name="q"/> inside (or within 5 cm of) the
        /// fan triangle node, a, b — a triangle anticlockwise about the node
        /// with less than half a turn in it, as every fan triangle is?</summary>
        static bool InFanTri(Vector2 c, Vector2 a, Vector2 b, Vector2 q)
        {
            if (Cross2(a - c, b - c) <= 1e-4f) return false;
            const float slack = 0.05f;
            float Side(Vector2 p0, Vector2 p1) => Cross2(p1 - p0, q - p0) / Mathf.Max((p1 - p0).magnitude, 1e-4f);
            return Side(c, a) >= -slack && Side(a, b) >= -slack && Side(b, c) >= -slack;
        }

        /// <summary>
        /// A fan's perimeter, anticlockwise round the node: each arm's two
        /// ribbon corners at its own trim and height (+ FanProudM), relative
        /// to <paramref name="origin"/>, arm after arm — the two corners of
        /// one arm always adjacent, the pair a road mouth.
        ///
        /// The corners were sorted by their OWN angles, and an arm that bends
        /// inside its trim put a corner past its neighbour's: link e343
        /// merging into Tyvola Road reaches the node at 61 degrees and its
        /// trim at 34, so its inner corner sorted between Tyvola's two, the
        /// mouth pair was split, and the fan from the centre left a notch a
        /// metre deep across Tyvola's lanes (onto the land a metre down), with
        /// a verge and kerb drawn across both mouths. 551 of 5119 fans
        /// interleaved so, and the probe of every fan mouth in the city found
        /// land or nothing under 637 arms' lanes. Sorted by ARM, a corner
        /// that pokes back past its neighbour's is resolved by the envelope
        /// of the two mouths (<see cref="PokeEnvelope"/>); one that pokes
        /// right past both of the neighbour's corners makes a stretch running
        /// backwards round the node, flagged <see cref="FanCorner.mouthNext"/>,
        /// and <see cref="FanTriangles"/> cuts that ring into ears.
        ///
        /// Between two arms whose edge lines, carried back from the corners,
        /// meet OUTSIDE the chord — the outside of a bend, where the chord cut
        /// across the lanes (on 79 fans past half a turn it folded over the
        /// others and faced down, leaving the whole outside unpaved) — the
        /// perimeter follows the edge lines to where they meet, or bevels
        /// them where that is further back than a trim and a half-width.
        /// </summary>
        /// <summary>How far back from its mouth an arm's edge line is carried
        /// where the chord to the next arm would cut its corner (FanCorners).</summary>
        const float MouthStripM = 1.5f;

        static void FanCorners(CityMap map, Trims trims, int n, Vector3 origin, List<FanCorner> corners)
        {
            corners.Clear();
            if (trims.bulb != null && trims.bulb[n]) { BulbCorners(map, trims, n, origin, corners); return; }
            // a junction cluster (roads pass L7, plan A11): one ring round all
            // its members' outside arms, about their centroid
            var cluster = trims.ClusterOfNode(n);
            fanArmIn.Clear();
            if (cluster == null) foreach (var ei in map.nodeEdges[n]) fanArmIn.Add((ei, n));
            else
                foreach (int m in cluster.nodes)
                    foreach (var ei in map.nodeEdges[m])
                        if (!trims.Internal(ei)) fanArmIn.Add((ei, m));
            FanCentre(map, trims, n, out var np, out _);
            var arms = fanArmScratch;
            arms.Clear();
            int drawn = 0;
            foreach (var (ei, an) in fanArmIn)
            {
                var e = map.edges[ei];
                if (e.a == e.b) continue;
                if (!ArmCollapsedAtTrim(map, trims, e, an)) drawn++;
            }
            foreach (var (ei, an) in fanArmIn)
            {
                var e = map.edges[ei];
                if (e.a == e.b) continue;
                // A branch clipped to nothing at its trim — its whole ribbon
                // there lies inside the host it is clipped against — has no
                // mouth, and its nominal corners stood in the host's stub:
                // the link e14103 inside East 11th Street poked both its
                // neighbours and left a notch in the host's lanes.
                if (drawn >= 2 && ArmCollapsedAtTrim(map, trims, e, an)) continue;
                float trim = trims.TrimAt(e, an);
                float at = e.a == an ? trim : e.length - trim;
                var p = e.PointAt(at);
                var right = RightAt(map, trims, e, at, out float widen);
                // the line model's corners: p - right * eMinus, p + right * ePlus
                LineModel.Extents(e, at, out float eMinus, out float ePlus);
                eMinus *= widen; ePlus *= widen;
                float hw = Mathf.Max(eMinus, ePlus);
                var tan = e.TangentAt(at);
                var outDir = e.a == an ? tan : -tan;
                // leaving the node, the corner on outDir's anticlockwise side is the later one round it
                int lateSide = Vector2.Dot(right, new Vector2(-outDir.y, outDir.x)) >= 0f ? 1 : -1;
                var toP = p - np;
                Vector2 cPlus = p + right * ePlus, cMinus = p - right * eMinus;
                arms.Add(new FanArm
                {
                    edge = ei, node = an, yNode = map.nodeY[an], lateSide = lateSide, trim = trim, hw = hw, yArm = e.YAt(at), outDir = outDir,
                    ang = toP.sqrMagnitude > 0.25f ? Mathf.Atan2(toP.y, toP.x) : Mathf.Atan2(outDir.y, outDir.x),
                    early = lateSide > 0 ? cMinus : cPlus, late = lateSide > 0 ? cPlus : cMinus,
                });
            }
            arms.Sort((a, b) => a.ang != b.ang ? a.ang.CompareTo(b.ang) : a.edge.CompareTo(b.edge));

            // An arm whose whole mouth lies under the fan its two neighbours
            // make without it — a few-metre OSM stub as wide as the road it
            // sits in (Carson Boulevard's 6.9 m e18571 inside South Tryon
            // Street) — adds nothing to the perimeter but a dent: its ribbon
            // starts on the fan. Only between the two halves of a road going
            // straight through, whose chord then runs along that road's edge;
            // between any other pair the chord cut back across their lanes.
            for (int i = 0; arms.Count > 3 && i < arms.Count; i++)
            {
                var P = arms[(i + arms.Count - 1) % arms.Count];
                var B = arms[i];
                var N = arms[(i + 1) % arms.Count];
                if (Vector2.Dot(P.outDir, N.outDir) >= ThroughCos) continue;
                bool Under(Vector2 q) =>
                    InFanTri(np, P.early, P.late, q) || InFanTri(np, P.late, N.early, q) || InFanTri(np, N.early, N.late, q);
                if (!Under(B.early) || !Under(B.late)) continue;
                arms.RemoveAt(i);
                // the angle it covered, with any arm already buried either side of it
                var bLate = N.afterBuried ? N.buriedLate : B.late;
                N.buriedEarly = B.afterBuried ? B.buriedEarly : B.early;
                N.buriedLate = bLate;
                N.afterBuried = true;
                arms[i % arms.Count] = N;
                i = -1;
            }

            int NodeOf(int edge)
            {
                foreach (var q in arms) if (q.edge == edge) return q.node;
                return n;
            }
            void Add(Vector2 c, float yArm, int edge, int side, bool mouthNext, bool extra, bool arc = false) =>
                corners.Add(new FanCorner
                {
                    ang = Mathf.Atan2(c.y - np.y, c.x - np.x),
                    pos = new Vector3(c.x - origin.x, yArm + FanProudM, c.y - origin.z),
                    edge = edge, side = side, yArm = yArm, mouthNext = mouthNext, extra = extra, node = NodeOf(edge), arc = arc,
                });
            // the height of the arm's surface carried back d metres from its trim toward the node
            float Back(FanArm A, float d) => Mathf.Lerp(A.yArm, A.yNode, d / Mathf.Max(A.trim, 0.01f));
            // THE CURB RETURN (roads pass L7, plan A10): the corner between
            // A's edge line and B's, meeting at X (dA back from A's corner along
            // A, dB from B's), rounded by an arc tangent to both, its radius the
            // class pair's or what the room from X to the nearer corner allows
            bool CurbArc(FanArm A, FanArm B, Vector2 X, float dA, float dB, int earlyB)
            {
                var eA = map.edges[A.edge]; var eB = map.edges[B.edge];
                if (trims.BranchAt(eA, A.node) >= 0 || trims.BranchAt(eB, B.node) >= 0) return false;
                float phi = Mathf.Acos(Mathf.Clamp(Vector2.Dot(A.outDir, B.outDir), -1f, 1f));
                if (phi < CurbMinPhiDeg * Mathf.Deg2Rad || phi > CurbMaxPhiDeg * Mathf.Deg2Rad) return false;
                float tanH = Mathf.Tan(phi * 0.5f);
                // a tenth of a metre of each edge line kept straight before the
                // arc, so the arc's first piece has a straight one to meet
                float R = CurbRadiusAt(eA, A.node, eB, B.node, phi), room = Mathf.Min(dA, dB) - 0.1f;
                if (R <= 0f) return false;   // one road going on: no curb there
                float rEff = Mathf.Min(R, tanH * Mathf.Min(room, CurbMaxTangentM));
                // the edge lines meet past a mouth (the trims never grew): square
                bool ok = rEff >= CurbMinR && dA > 0.05f && dB > 0.05f;
                CurbLog?.Add(new CurbRec { node = n, edgeA = A.edge, edgeB = B.edge, phiDeg = phi * Mathf.Rad2Deg, r = R, rEff = ok ? rEff : 0f, room = room, arc = ok });
                if (!ok) return false;
                float L = rEff / tanH;
                Vector2 TA = X + A.outDir * L, TB = X + B.outDir * L;
                var bis = (A.outDir + B.outDir).normalized;
                var C = X + bis * (rEff / Mathf.Sin(phi * 0.5f));
                float yA = Back(A, dA - L), yB = Back(B, dB - L);
                float a0 = Mathf.Atan2(TA.y - C.y, TA.x - C.x), a1 = Mathf.Atan2(TB.y - C.y, TB.x - C.x);
                float sweep = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                float step = 2f * Mathf.Acos(Mathf.Clamp01(1f - CurbSagM / rEff));
                int pieces = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(sweep) / Mathf.Max(step, 1e-3f)), 1, CurbMaxPieces);
                Add(TA, yA, A.edge, A.lateSide, false, true, true);
                for (int k = 1; k < pieces; k++)
                {
                    float f = (float)k / pieces, ang = a0 + sweep * f;
                    var q = C + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rEff;
                    if (f < 0.5f) Add(q, Mathf.Lerp(yA, yB, f), A.edge, A.lateSide, false, true, true);
                    else Add(q, Mathf.Lerp(yA, yB, f), B.edge, earlyB, false, true, true);
                }
                Add(TB, yB, B.edge, earlyB, false, true, true);
                return true;
            }

            // The ring is built pair by pair, from each arm's late corner to the
            // next arm's early corner (or what stands in for them), so the
            // wrap-round pair closes it on the first arm's early corner and the
            // stretch from each pair's last point to the next pair's first is
            // an arm's mouth.
            if (arms.Count < 2) return;
            for (int i = 0; i < arms.Count; i++)
            {
                var A = arms[i];
                var B = arms[(i + 1) % arms.Count];
                int earlyB = -B.lateSide;
                Vector2 a = A.late, b = B.early, ab = b - a;
                Vector2 cA = a - np, cB = b - np;

                if (B.afterBuried)
                {
                    // The chord runs along the road going through; across the
                    // buried arm's own angle its ribbon lies beyond, and no
                    // verge may stand there (it stood over the next fan along
                    // it). Either side of that the chord is a free edge.
                    float ue = ChordOnRay(a, b, np, B.buriedEarly), ul = ChordOnRay(a, b, np, B.buriedLate);
                    float len = ab.magnitude;
                    bool freeIn = ue * len > 0.05f, freeOut = (1f - ul) * len > 0.05f && ul > ue;
                    Add(a, A.yArm, A.edge, A.lateSide, !freeIn, false);
                    if (freeIn) Add(a + ab * ue, Mathf.Lerp(A.yArm, B.yArm, ue), A.edge, A.lateSide, true, true);
                    if (freeOut) Add(a + ab * ul, Mathf.Lerp(A.yArm, B.yArm, ul), B.edge, earlyB, false, true);
                    Add(b, B.yArm, B.edge, earlyB, true, false);
                    continue;
                }
                if (Cross2(cA, cB) < 0f && Vector2.Dot(cA, cB) > 0f &&
                    Cross2(A.early - np, cB) >= 0f && Cross2(cA, B.late - np) >= 0f &&
                    PokeEnvelope(A, B, np, origin, earlyB, corners))
                    continue;

                Add(a, A.yArm, A.edge, A.lateSide, false, false);
                bool carried = false;
                float den = Cross2(A.outDir, B.outDir);
                if (Mathf.Abs(den) > 1e-3f)
                {
                    // a + A.outDir * ta = b + B.outDir * tb: both negative is behind both mouths
                    float ta = Cross2(ab, B.outDir) / den;
                    float tb = Cross2(ab, A.outDir) / den;
                    // the inside of a corner (anything but the outside of a bend):
                    // a curb return, where neither arm is clipped (plan A10)
                    bool outsideBend = ta < -0.05f && tb < -0.05f && Cross2(ab, A.outDir * ta) < -1e-3f;
                    if (ArcsOn && !outsideBend && CurbArc(A, B, a + A.outDir * ta, -ta, -tb, earlyB))
                        carried = true;
                    else if (outsideBend)
                    {
                        if (-ta <= A.trim + A.hw && -tb <= B.trim + B.hw)
                        {
                            Add(a + A.outDir * ta, (Back(A, -ta) + Back(B, -tb)) * 0.5f, A.edge, A.lateSide, false, true);
                            carried = true;
                        }
                        else
                        {
                            // too far back for a mitre: bevel, first at a trim and a
                            // half-width back, then at the node's own cross-line
                            for (int k = 1; k >= 0 && !carried; k--)
                            {
                                float da = Mathf.Min(-ta, A.trim + A.hw * k), db = Mathf.Min(-tb, B.trim + B.hw * k);
                                var a2 = a - A.outDir * da; var b2 = b - B.outDir * db;
                                if (Cross2(a2 - np, b2 - np) < -1e-3f) continue;
                                if (da > 0.05f) Add(a2, Back(A, da), A.edge, A.lateSide, false, true);
                                if (db > 0.05f) Add(b2, Back(B, db), B.edge, earlyB, false, true);
                                carried = true;
                            }
                        }
                    }
                }
                // THE MOUTH STRIPS (WP-11b): a lane added on one side puts an
                // arm's corner further out than its neighbour's, and a chord
                // from corner to corner then cut back across the first metre
                // of that arm's mouth - land, or the chord's rail, over its
                // outer lane (North Tryon at Keswick, North Caldwell at 12th).
                // Each arm's edge line is carried back MouthStripM toward the
                // node wherever the chord would cut it.
                if (!carried && Cross2(cA, cB) >= 0f)
                {
                    float dA = Mathf.Min(MouthStripM, A.trim * 0.5f), dB = Mathf.Min(MouthStripM, B.trim * 0.5f);
                    var a2 = a - A.outDir * dA; var b2 = b - B.outDir * dB;
                    bool cutA = dA > 0.05f && Cross2(ab, a2 - a) < -1e-3f;
                    bool cutB = dB > 0.05f && Cross2(ab, b2 - a) < -1e-3f;
                    if (cutA) Add(a2, Back(A, dA), A.edge, A.lateSide, false, true);
                    if (cutB) Add(b2, Back(B, dB), B.edge, earlyB, false, true);
                }
                // a corner poked right past the neighbour's other corner too:
                // no envelope, and no free edge — FanTriangles cuts the ring into ears
                if (!carried && Cross2(cA, cB) < 0f && Vector2.Dot(cA, cB) > 0f)
                {
                    var last = corners[corners.Count - 1];
                    last.mouthNext = true;
                    corners[corners.Count - 1] = last;
                }
                Add(b, B.yArm, B.edge, earlyB, true, false);
            }
            // pair 0 began on arm 0's late corner; the ring now ends on its early one

            // A cluster's inside edges (plan A11) draw no ribbon: where one is
            // wider than the chords between its neighbours (Briar Creek Road's
            // 11.8 m piece between nodes 10702 and 11156, its turn lanes past
            // the arms either side) the ring is carried out round its edges.
            if (cluster != null && corners.Count >= 3)
                foreach (int ei in cluster.inner)
                {
                    var e = map.edges[ei];
                    for (int q = 0; q <= 4; q++)
                    {
                        float s = e.length * q * 0.25f;
                        var p = e.PointAt(s);
                        var tn = e.TangentAt(s);
                        var rgt = new Vector2(-tn.y, tn.x);
                        LineModel.Extents(e, s, out float eM, out float eP);
                        RingTakeIn(corners, np, origin, p - rgt * eM, e.YAt(s), ei, -1, e.a);
                        RingTakeIn(corners, np, origin, p + rgt * eP, e.YAt(s), ei, 1, e.a);
                    }
                }
        }

        /// <summary>Carries a ring out to a point beyond one of its free
        /// chords (seen from the centre): the point goes in between that
        /// chord's corners, a curb-return-like vertex (its verge on the
        /// bisector, no corner fill). Not past a road mouth (that arm's
        /// ribbon is there).</summary>
        static void RingTakeIn(List<FanCorner> ring, Vector2 c, Vector3 origin, Vector2 q, float y, int edge, int side, int node)
        {
            var d = q - c;
            if (d.sqrMagnitude < 1e-4f) return;
            for (int i = 0; i < ring.Count; i++)
            {
                var k0 = ring[i]; var k1 = ring[(i + 1) % ring.Count];
                Vector2 a = new Vector2(k0.pos.x + origin.x, k0.pos.z + origin.z), b = new Vector2(k1.pos.x + origin.x, k1.pos.z + origin.z);
                var ab = b - a;
                float den = Cross2(d, ab);
                if (Mathf.Abs(den) < 1e-6f) continue;
                // c + d t = a + ab u
                float t = Cross2(a - c, ab) / den, u = Cross2(a - c, d) / den;
                if (u < 0f || u > 1f || t <= 0f || t >= 1f) continue;
                if ((1f - t) * d.magnitude < 0.05f) return;   // on the chord already
                if (k0.mouthNext) return;                      // past an arm's mouth: its ribbon
                ring.Insert(i + 1, new FanCorner
                {
                    ang = Mathf.Atan2(d.y, d.x),
                    pos = new Vector3(q.x - origin.x, y + FanProudM, q.y - origin.z),
                    edge = edge, side = side, yArm = y, mouthNext = false, extra = true, node = node, arc = true,
                });
                return;
            }
        }

        /// <summary>
        /// Where the next arm's early corner lies back round the node past
        /// this arm's late corner, the two mouth wedges overlap, and the
        /// perimeter over the overlap is their outer envelope: along the
        /// mouth that stands further out, stepping to the other where it
        /// takes over (down the corner's edge line where that meets the other
        /// mouth, else along the ray from the node), crossing where the
        /// mouths cross. Every mouth is then
        /// wholly under one wedge or the other, whether or not the poking
        /// arm draws its ribbon out to its full width (a clipped branch does
        /// not), and the ring stays a star about the node. None of the
        /// envelope is a free edge. False where a ray misses a mouth line.
        /// </summary>
        static bool PokeEnvelope(FanArm A, FanArm B, Vector2 c, Vector3 origin, int earlyB, List<FanCorner> corners)
        {
            Vector2 AE = A.early - c, AL = A.late - c, BE = B.early - c, BL = B.late - c;
            Vector2 dA = AL - AE, dB = BL - BE;
            float d0 = Cross2(BE, dA), d1 = Cross2(AL, dB), dX = Cross2(dA, dB);
            if (Mathf.Abs(d0) < 1e-5f || Mathf.Abs(d1) < 1e-5f) return false;
            float tA0 = Cross2(AE, dA) / d0;     // A's mouth on the ray through B's early corner (which is at 1)
            float tB1 = Cross2(BE, dB) / d1;     // B's mouth on the ray through A's late corner (which is at 1)
            bool aOut0 = tA0 >= 1f, aOut1 = tB1 <= 1f;
            var yA0 = c + BE * tA0;              // on A's mouth, behind or beyond B's early corner
            var xB1 = c + AL * tB1;              // on B's mouth, behind or beyond A's late corner
            // Better than the radial step, where it lands on the other mouth:
            // the step down the corner's own EDGE LINE, which keeps the whole
            // of that arm's lanes carried back from its mouth under the fan,
            // where the wedge narrows toward the centre.
            float dAo = Cross2(A.outDir, dB);
            if (Mathf.Abs(dAo) > 1e-5f)
            {
                float u = Cross2(BE - AL, dB) / dAo, v = Cross2(BE - AL, A.outDir) / dAo;
                var x = AL + A.outDir * u;
                if (u <= 0f && v >= 0f && v <= 1f && Cross2(AL, x) >= -1e-3f && Cross2(x, BL) >= -1e-3f) xB1 = c + x;
            }
            float dBo = Cross2(B.outDir, dA);
            if (Mathf.Abs(dBo) > 1e-5f)
            {
                float u = Cross2(AE - BE, dA) / dBo, v = Cross2(AE - BE, B.outDir) / dBo;
                var y = BE + B.outDir * u;
                if (u <= 0f && v >= 0f && v <= 1f && Cross2(AE, y) >= -1e-3f && Cross2(y, BE) >= -1e-3f) yA0 = c + y;
            }
            void Put(Vector2 p, float y, int edge, int side, bool extra) =>
                corners.Add(new FanCorner
                {
                    ang = Mathf.Atan2(p.y - c.y, p.x - c.x),
                    pos = new Vector3(p.x - origin.x, y + FanProudM, p.y - origin.z),
                    edge = edge, side = side, yArm = y, mouthNext = true, extra = extra, node = edge == A.edge ? A.node : B.node,
                });
            if (aOut0 && aOut1)
            {
                // A's mouth further out across the overlap: down A's radial to B's mouth
                Put(A.late, A.yArm, A.edge, A.lateSide, false);
                Put(xB1, B.yArm, B.edge, earlyB, true);
            }
            else if (!aOut0 && !aOut1)
            {
                // B's: from A's mouth out along B's early radial
                Put(yA0, A.yArm, A.edge, A.lateSide, true);
                Put(B.early, B.yArm, B.edge, earlyB, false);
            }
            else
            {
                if (Mathf.Abs(dX) < 1e-5f) return false;
                var z = A.early + dA * (Cross2(BE - AE, dB) / dX);   // where the two mouths cross
                float yZ = (A.yArm + B.yArm) * 0.5f;
                if (aOut0)
                {
                    Put(z, yZ, A.edge, A.lateSide, true);
                    Put(z, yZ, B.edge, earlyB, true);
                }
                else
                {
                    Put(yA0, A.yArm, A.edge, A.lateSide, true);
                    Put(B.early, B.yArm, B.edge, earlyB, false);
                    Put(z, yZ, A.edge, A.lateSide, true);
                    Put(A.late, A.yArm, A.edge, A.lateSide, false);
                    Put(xB1, B.yArm, B.edge, earlyB, true);
                }
            }
            return true;
        }

        static readonly List<int> fanTriScratch = new List<int>(48);
        static readonly List<Vector3> fanSteinerScratch = new List<Vector3>(8), fanPolySteiner = new List<Vector3>(8);
        static readonly List<int> earScratch = new List<int>(16);
        static readonly List<Vector2> earPlan = new List<Vector2>(16);

        /// <summary>
        /// Triangles over a fan's perimeter, as index triples anticlockwise in
        /// map view: 0 is the node centre, i + 1 corner i. A perimeter that is
        /// a star about the node — every stretch turning anticlockwise round
        /// it — fans from the centre at the node's height, as every fan did.
        /// One that is not (a corner poked right past both of a neighbour's)
        /// is cut into ears between its corners, so no two triangles lie over one
        /// patch of ground at two heights and none folds face-down; if even
        /// that fails on a perimeter that crosses itself, the centre fan
        /// without its folded triangles.
        /// </summary>
        static void FanTriangles(List<FanCorner> corners, Vector2 centre, List<int> tris)
        {
            tris.Clear();
            int count = corners.Count;
            bool star = true;
            for (int i = 0; i < count && star; i++)
            {
                var a = new Vector2(corners[i].pos.x, corners[i].pos.z) - centre;
                var b = new Vector2(corners[(i + 1) % count].pos.x, corners[(i + 1) % count].pos.z) - centre;
                if ((b - a).sqrMagnitude > 1e-4f && Cross2(a, b) < -1e-3f) star = false;
            }
            if (!star)
            {
                earScratch.Clear(); earPlan.Clear();
                for (int i = 0; i < count; i++)
                {
                    var p = new Vector2(corners[i].pos.x, corners[i].pos.z);
                    if (earScratch.Count > 0 && (p - earPlan[earScratch[earScratch.Count - 1]]).sqrMagnitude < 1e-4f) { earPlan.Add(p); continue; }
                    earPlan.Add(p);
                    earScratch.Add(i);
                }
                if (earScratch.Count > 1 && (earPlan[earScratch[0]] - earPlan[earScratch[earScratch.Count - 1]]).sqrMagnitude < 1e-4f)
                    earScratch.RemoveAt(earScratch.Count - 1);
                int guard = earScratch.Count * earScratch.Count + 16;
                while (earScratch.Count > 3 && guard-- > 0)
                {
                    bool clipped = false;
                    for (int i = 0; i < earScratch.Count; i++)
                    {
                        int i0 = earScratch[(i + earScratch.Count - 1) % earScratch.Count];
                        int i1 = earScratch[i];
                        int i2 = earScratch[(i + 1) % earScratch.Count];
                        Vector2 a = earPlan[i0], b = earPlan[i1], c = earPlan[i2];
                        if (Cross2(b - a, c - a) <= 1e-4f) continue;
                        bool holds = true;
                        foreach (var j in earScratch)
                        {
                            if (j == i0 || j == i1 || j == i2) continue;
                            if (InTri(a, b, c, earPlan[j])) { holds = false; break; }
                        }
                        if (!holds) continue;
                        tris.Add(i0 + 1); tris.Add(i1 + 1); tris.Add(i2 + 1);
                        earScratch.RemoveAt(i);
                        clipped = true;
                        break;
                    }
                    if (!clipped) break;
                }
                if (earScratch.Count == 3 && Cross2(earPlan[earScratch[1]] - earPlan[earScratch[0]], earPlan[earScratch[2]] - earPlan[earScratch[0]]) > 1e-4f)
                {
                    tris.Add(earScratch[0] + 1); tris.Add(earScratch[1] + 1); tris.Add(earScratch[2] + 1);
                    return;
                }
                if (earScratch.Count < 3) return;
                tris.Clear();
            }
            for (int i = 0; i < count; i++)
            {
                var a = new Vector2(corners[i].pos.x, corners[i].pos.z) - centre;
                var b = new Vector2(corners[(i + 1) % count].pos.x, corners[(i + 1) % count].pos.z) - centre;
                if (Cross2(a, b) <= 1e-4f) continue;   // degenerate, or folded face-down
                tris.Add(0); tris.Add(i + 1); tris.Add((i + 1) % count + 1);
            }
        }

        /// <summary>For the audits: a fan's open chords (world space, the
        /// outward normal) and whether the tile puts it on structure. Empty
        /// where the node draws no fan.</summary>
        /// <summary>For the diagnostics: a fan's corners, anticlockwise, as
        /// FanCorners lays them (plan x, z; the arm and side; whether the chord
        /// to the next is a road mouth).</summary>
        public static string DescribeFan(CityMap map, Trims trims, int n)
        {
            var corners = new List<FanCorner>(12);
            FanCorners(map, trims, n, Vector3.zero, corners);
            var sb = new System.Text.StringBuilder();
            foreach (var k in corners)
                sb.AppendLine($"    corner ({k.pos.x:0.00}, {k.pos.z:0.00}) e{k.edge} side {k.side}{(k.mouthNext ? " MOUTH->" : "")}{(k.extra ? " extra" : "")}");
            return sb.ToString();
        }

        public static bool FanPerimeter(CityMap map, Trims trims, int n, List<(Vector3 a, Vector3 b, Vector2 outward)> chords)
        {
            chords.Clear();
            if (!trims.patch[n]) return false;
            // a junction cluster's ring is its lowest member's (plan A11)
            if (FanKey(trims, n) != n) return false;
            var corners = new List<FanCorner>(12);
            FanCorners(map, trims, n, Vector3.zero, corners);
            if (corners.Count < 3) return false;
            var np = map.nodes[n];
            for (int i = 0; i < corners.Count; i++)
            {
                var k0 = corners[i]; var k1 = corners[(i + 1) % corners.Count];
                if (k0.mouthNext) continue;
                var chord = new Vector2(k1.pos.x - k0.pos.x, k1.pos.z - k0.pos.z);
                if (chord.sqrMagnitude < 0.0025f) continue;
                // the perimeter runs anticlockwise: outward is its right
                chords.Add((k0.pos, k1.pos, new Vector2(chord.y, -chord.x).normalized));
            }
            return FanOnStructure(map, trims, n);
        }

        /// <summary>For the audits: where an edge goes onto or off structure,
        /// the ends the approach rails are carried past.</summary>
        public static List<float> StructureEndsOf(CityMap map, Trims trims, CityMap.Edge e)
        {
            var ends = new List<float>(4);
            StructureEnds(map, trims, e, ends);
            return ends;
        }

        /// <summary>The shoulder a ribbon side's verge starts with: wider on
        /// a freeway or expressway's outside.</summary>
        static float ShoulderOf(CityMap.Edge e, int side) =>
            (side < 0 || !e.oneway) && !e.link && e.cls >= 4 ? FreewayShoulderM : VergeShoulderM;

        /// <summary>At a fan corner, the arm's side verge runs out square to
        /// the arm and the chord's square to the chord; the wedge between the
        /// two cross-sections is filled, or the lattice showed through there
        /// an inch and a sink below the corner.</summary>
        static void CornerFill(CityMap map, Trims trims, TileMeshes tm, int node, Vector3 corner, float yArm, CityMap.Edge arm, int side, Vector2 chordOut)
        {
            var cW = new Vector2(corner.x + tm.origin.x, corner.z + tm.origin.z);
            float at = arm.a == node ? trims.atA[arm.index] : arm.length - trims.atB[arm.index];
            if (arm.ElevatedAt(at)) return;
            var tan = arm.TangentAt(at);
            var armOut = new Vector2(-tan.y, tan.x) * side;
            if (Vector2.Dot(armOut, chordOut) > 0.999f) return;
            GatherNear(map, cW, cW, VergeMaxRunM + RoadsideRules.ToeTuckRunM);
            var shArm = new StripShape { shoulder = ShoulderOf(arm, side), maxRun = VergeMaxRunM };
            var shChord = new StripShape { shoulder = VergeShoulderM, maxRun = VergeMaxRunM };
            // An arm side the verge cannot grade stands on a retaining face
            // (DropFrom): there is no arm verge for the wedge to meet, and a
            // fill from the corner out to the chord's verge lay as a plate
            // 0.9 m over the next road's verge (node 5211: e9968's corner a
            // quarter of a metre from e7753's and 1.2 m above it). Asked as
            // DropAt asks it of that side, so a freeway median behind its
            // Jersey barrier, which keeps its verge, keeps its fill too.
            bool armLaid = SolveStrip(map, trims, cW, yArm, armOut, shArm, profPrev, out bool armGraded, out float armStopped);
            if (!armLaid || Ungraded(armLaid, armGraded, armStopped, cW, yArm, profPrev, !MedianBarriered(arm, side))) return;
            if (!SolveStrip(map, trims, cW, corner.y, chordOut, shChord, profCur)) return;
            for (int q = 0; q < 3; q++)
            {
                FillTri(map, tm, profPrev[q], profPrev[q + 1], profCur[q + 1]);
                FillTri(map, tm, profPrev[q], profCur[q + 1], profCur[q]);
            }
        }

        /// <summary>One triangle of a corner fill, facing up — left out where
        /// it stands steeper than <see cref="FillSteepNy"/>. The two cross-
        /// sections meet at one corner, and where one ends in a tuck straight
        /// down (stopped by another road) or the two run out almost parallel,
        /// the triangle between them is a fin: the body box met one 1.9 m
        /// tall between two ramps 1.33 m apart (e7753 under e9968) and a 9 cm
        /// one between East 3rd Street's carriageways.</summary>
        static void FillTri(CityMap map, TileMeshes tm, Vector3 a, Vector3 b, Vector3 c)
        {
            if (Fin(a, b, c)) return;
            groundLog?.Add((groundTag, a, b, c));
            var n = Vector3.Cross(b - a, c - a);
            var cen = (a + b + c) / 3f;
            bool paved = PavedAt(map, cen.x, cen.z);
            // Tri(a, b, c) draws (a, c, b), whose normal is Cross(c - a, b - a)
            if (n.y > 0f) { var t = b; b = c; c = t; }
            var o = tm.origin;
            GroundBucket(paved).Tri(a - o, b - o, c - o, GroundUV(paved, a.x, a.z), GroundUV(paved, b.x, b.z), GroundUV(paved, c.x, c.z));
        }

        // ------------------------------------------------------------------
        /// <summary>How far a procedural house or lot stands from a ravine's
        /// line (WP-04b): out of its floor and the foot of its banks.</summary>
        public const float RavineClearM = 12f;
        /// <summary>A creek's water sheet is cut into pieces this long, so its
        /// surface follows the bed and a road over a culvert can take a piece
        /// out.</summary>
        const float WaterPieceM = 8f;
        /// <summary>How far past the flat floor the sheet reaches, under the
        /// banks: the lattice is 8 m, so the banks meet the water between its
        /// vertices, never exactly at the floor's edge.</summary>
        const float WaterSheetPad = 8f;
        static readonly HashSet<int> waterRoadScratch = new HashSet<int>();

        /// <summary>
        /// A creek's water (WP-04b): a sheet at the surface over its bed
        /// (<see cref="CityElevation.CreekSurfaceY"/>), from bank to bank:
        /// flat across the carved floor, then out under the banks, where the
        /// rising land hides its edges. An outer edge over land lower than the
        /// water (a confluence, a road's cut) comes down to that land instead
        /// of hanging over it, and a piece over a grounded road's pavement
        /// lower than the water is left out: water never stands on a road.
        /// </summary>
        static void BuildCreek(CityMap map, TileMeshes tm, Vector2 min, Vector2 max, CityMap.Water w, Bucket bk)
        {
            float flat = CityElevation.CreekFlatHalf(w), reach = flat + WaterSheetPad;
            var o = tm.origin;
            for (int i = 0; i + 1 < w.pts.Length; i++)
            {
                Vector2 a = w.pts[i], b = w.pts[i + 1];
                float len = Vector2.Distance(a, b);
                if (len < 0.01f) continue;
                var dir = (b - a) / len;
                var right = new Vector2(dir.y, -dir.x);
                // WP-25: the sheet's sides MITRED at every bend of the line, so
                // one piece's edge meets the next's (square per segment, the
                // outside of each bend opened a notch the lattice showed
                // through and the inside overlapped); off in the A/B
                Vector2 rA = right, rB = right;
                if (!HydroOff) { rA = CreekMitre(w, i, right); rB = CreekMitre(w, i + 1, right); }
                int n = Mathf.Max(1, Mathf.CeilToInt(len / WaterPieceM));
                for (int k = 0; k < n; k++)
                {
                    float t0 = (float)k / n, t1 = (float)(k + 1) / n;
                    Vector2 p0 = Vector2.Lerp(a, b, t0), p1 = Vector2.Lerp(a, b, t1);
                    Vector2 q0 = Vector2.Lerp(rA, rB, t0), q1 = Vector2.Lerp(rA, rB, t1);
                    var mid = (p0 + p1) * 0.5f;
                    if (mid.x < min.x || mid.x >= max.x || mid.y < min.y || mid.y >= max.y) continue;
                    float s0 = w.s[i] + len * t0, s1 = w.s[i] + len * t1;
                    float y0 = CityElevation.CreekSurfaceY(w, s0, p0), y1 = CityElevation.CreekSurfaceY(w, s1, p1);
                    float v0 = s0 / 40f, v1 = s1 / 40f;
                    // water never stands on a grounded road lower than it
                    if (WaterOnRoad(map, mid, Mathf.Max(y0, y1))) continue;
                    // columns: -reach, -flat, +flat, +reach
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector2 i0 = p0 + q0 * (flat * side), i1 = p1 + q1 * (flat * side);
                        Vector2 e0 = p0 + q0 * (reach * side), e1 = p1 + q1 * (reach * side);
                        float ye0 = Mathf.Min(y0, LatticeY(map, e0.x, e0.y) - 0.05f);
                        float ye1 = Mathf.Min(y1, LatticeY(map, e1.x, e1.y) - 0.05f);
                        if (WaterOnRoad(map, (i0 + i1 + e0 + e1) * 0.25f, Mathf.Max(y0, y1))) continue;   // an outer piece over a road beside the creek
                        float uIn = 0.5f + 0.5f * side * flat / reach, uOut = 0.5f + 0.5f * side;
                        var A = new Vector3(i0.x - o.x, y0, i0.y - o.z); var B = new Vector3(i1.x - o.x, y1, i1.y - o.z);
                        var C = new Vector3(e1.x - o.x, ye1, e1.y - o.z); var D = new Vector3(e0.x - o.x, ye0, e0.y - o.z);
                        bk.Up(A, B, C, D, new Vector2(uIn, v0), new Vector2(uIn, v1), new Vector2(uOut, v1), new Vector2(uOut, v0));
                    }
                    // the floor between the two inner columns
                    {
                        Vector2 l0 = p0 - q0 * flat, l1 = p1 - q1 * flat, r0 = p0 + q0 * flat, r1 = p1 + q1 * flat;
                        float uL = 0.5f - 0.5f * flat / reach, uR = 0.5f + 0.5f * flat / reach;
                        bk.Up(new Vector3(r0.x - o.x, y0, r0.y - o.z), new Vector3(r1.x - o.x, y1, r1.y - o.z),
                                new Vector3(l1.x - o.x, y1, l1.y - o.z), new Vector3(l0.x - o.x, y0, l0.y - o.z),
                                new Vector2(uR, v0), new Vector2(uR, v1), new Vector2(uL, v1), new Vector2(uL, v0));
                    }
                    // the clay banks either side, where the water shows (WP-25)
                    EmitCreekBanks(map, tm, p0, p1, q0, q1, flat, (y0 + y1) * 0.5f);
                }
            }
        }

        /// <summary>Is there grounded pavement at p lower than a water
        /// surface at y (plus a margin)? Then the water would stand on the
        /// road: a creek under a road that is not a span (a culvert, WP-25) or
        /// a road beside a creek in its floodplain.</summary>
        static bool WaterOnRoad(CityMap map, Vector2 p, float y)
        {
            waterRoadScratch.Clear();
            map.EdgeSegsInRect(p - Vector2.one * 16f, p + Vector2.one * 16f, waterRoadScratch);
            foreach (int packed in waterRoadScratch)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                if (si + 1 >= e.pts.Length) continue;
                Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                if (Vector2.Distance(p, a + d * t) > e.HalfMax + 1.5f) continue;
                float at = e.s[si] + Mathf.Sqrt(L2) * t;
                if (e.ElevatedAt(at)) continue;
                if (e.YAt(at) < y + 0.5f) return true;
            }
            return false;
        }

        static void BuildWater(CityMap map, TileMeshes tm, Vector2 min, Vector2 max)
        {
            var bk = buckets[(int)Slot.Water];

            foreach (var w in map.waters)
            {
                if (w.ravine) continue;   // a ravine carries no water (WP-04b)
                if (w.bbMax.x < min.x - 60f || w.bbMin.x > max.x + 60f ||
                    w.bbMax.y < min.y - 60f || w.bbMin.y > max.y + 60f) continue;

                if (!w.lake && w.bedY != null) { BuildCreek(map, tm, min, max, w, bk); continue; }
                if (!w.lake)
                {
                    float hw = w.width * 0.5f;
                    for (int i = 0; i + 1 < w.pts.Length; i++)
                    {
                        var mid = (w.pts[i] + w.pts[i + 1]) * 0.5f;
                        if (mid.x < min.x || mid.x >= max.x || mid.y < min.y || mid.y >= max.y) continue;
                        var d = (w.pts[i + 1] - w.pts[i]);
                        float len = d.magnitude;
                        if (len < 0.01f) continue;
                        d /= len;
                        var right = new Vector2(-d.y, d.x) * hw;
                        var a = w.pts[i]; var b = w.pts[i + 1];
                        float ya = CityElevation.RiverSurfaceY(a.x, a.y);
                        float yb = CityElevation.RiverSurfaceY(b.x, b.y);
                        bk.Quad(
                            new Vector3(a.x - right.x - tm.origin.x, ya, a.y - right.y - tm.origin.z),
                            new Vector3(b.x - right.x - tm.origin.x, yb, b.y - right.y - tm.origin.z),
                            new Vector3(b.x + right.x - tm.origin.x, yb, b.y + right.y - tm.origin.z),
                            new Vector3(a.x + right.x - tm.origin.x, ya, a.y + right.y - tm.origin.z),
                            new Vector2(0f, i / 3f), new Vector2(0f, (i + 1) / 3f),
                            new Vector2(1f, (i + 1) / 3f), new Vector2(1f, i / 3f));
                    }
                }
                else
                {
                    polyScratch.Clear();
                    polyScratch.AddRange(w.pts);
                    ClipPoly(polyScratch, min, max);
                    if (polyScratch.Count >= 3)
                        EarcutInto(bk, polyScratch, w.surfaceY, tm.origin, 26f);
                }
            }
        }

        static void ClipPoly(List<Vector2> poly, Vector2 min, Vector2 max)
        {
            ClipHalf(poly, p => p.x >= min.x, (a, b) => LerpX(a, b, min.x));
            ClipHalf(poly, p => p.x <= max.x, (a, b) => LerpX(a, b, max.x));
            ClipHalf(poly, p => p.y >= min.y, (a, b) => LerpY(a, b, min.y));
            ClipHalf(poly, p => p.y <= max.y, (a, b) => LerpY(a, b, max.y));
        }
        static Vector2 LerpX(Vector2 a, Vector2 b, float x) =>
            Vector2.Lerp(a, b, Mathf.Abs(b.x - a.x) < 1e-6f ? 0f : (x - a.x) / (b.x - a.x));
        static Vector2 LerpY(Vector2 a, Vector2 b, float y) =>
            Vector2.Lerp(a, b, Mathf.Abs(b.y - a.y) < 1e-6f ? 0f : (y - a.y) / (b.y - a.y));

        static readonly List<Vector2> clipScratch = new List<Vector2>();
        static void ClipHalf(List<Vector2> poly, System.Func<Vector2, bool> inside,
                             System.Func<Vector2, Vector2, Vector2> cross)
        {
            clipScratch.Clear();
            for (int i = 0; i < poly.Count; i++)
            {
                var cur = poly[i];
                var prev = poly[(i + poly.Count - 1) % poly.Count];
                bool cIn = inside(cur), pIn = inside(prev);
                if (cIn)
                {
                    if (!pIn) clipScratch.Add(cross(prev, cur));
                    clipScratch.Add(cur);
                }
                else if (pIn) clipScratch.Add(cross(prev, cur));
            }
            poly.Clear();
            poly.AddRange(clipScratch);
        }

        /// <summary>Simple ear clipping, faces UP. Fine for the clipped lake
        /// pieces and for building roofs, which are small simple polygons.</summary>
        static void EarcutInto(Bucket bk, List<Vector2> poly, float y, Vector3 origin, float uvMeters)
        {
            var idx = new List<int>(poly.Count);
            float area = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                area += a.x * b.y - b.x * a.y;
            }
            bool ccw = area > 0f;
            for (int i = 0; i < poly.Count; i++) idx.Add(ccw ? i : poly.Count - 1 - i);

            int baseI = bk.v.Count;
            foreach (var p in poly)
            {
                bk.v.Add(new Vector3(p.x - origin.x, y, p.y - origin.z));
                bk.uv.Add(new Vector2(p.x / uvMeters, p.y / uvMeters));
            }

            int guard = poly.Count * poly.Count + 16;
            while (idx.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int i0 = idx[(i + idx.Count - 1) % idx.Count];
                    int i1 = idx[i];
                    int i2 = idx[(i + 1) % idx.Count];
                    var a = poly[i0]; var b = poly[i1]; var c = poly[i2];
                    if ((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y) <= 1e-7f) continue;
                    bool holds = true;
                    foreach (var j in idx)
                    {
                        if (j == i0 || j == i1 || j == i2) continue;
                        if (InTri(a, b, c, poly[j])) { holds = false; break; }
                    }
                    if (!holds) continue;
                    bk.t.Add(baseI + i0); bk.t.Add(baseI + i2); bk.t.Add(baseI + i1);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (idx.Count == 3)
            {
                bk.t.Add(baseI + idx[0]); bk.t.Add(baseI + idx[2]); bk.t.Add(baseI + idx[1]);
            }
        }

        static bool InTri(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0;
            bool pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        // ------------------------------------------------------------------
        //  Procedural frontage boxes (outside the footprint data) — CityWorld
        //  instantiates the prefab lots itself.
        // ------------------------------------------------------------------
        static void BuildBuildings(CityMap map, Dictionary<long, List<CityBuildings.B>> buildings,
                                   TileMeshes tm, int tx, int tz)
        {
            long key = ((long)tx << 24) ^ (tz & 0xFFFFFF);
            if (buildings == null || !buildings.TryGetValue(key, out var list)) return;

            foreach (var b in list)
            {
                if (b.kind != 0) continue;

                float cy = Mathf.Cos(b.yaw), sy = Mathf.Sin(b.yaw);
                Vector2 fwd = new Vector2(sy, cy);           // faces the road
                Vector2 rgt = new Vector2(cy, -sy);
                Vector2 hw = rgt * (b.w * 0.5f);
                Vector2 hd = fwd * (b.d * 0.5f);

                var c1 = b.pos + hw + hd; var c2 = b.pos - hw + hd;
                var c3 = b.pos - hw - hd; var c4 = b.pos + hw - hd;
                float g = Mathf.Min(
                    Mathf.Min(CityElevation.GroundY(map, c1.x, c1.y), CityElevation.GroundY(map, c2.x, c2.y)),
                    Mathf.Min(CityElevation.GroundY(map, c3.x, c3.y), CityElevation.GroundY(map, c4.x, c4.y)));
                float y0 = g - BuildingSink;
                float y1 = y0 + b.h + BuildingSink;

                if (b.gable)
                {
                    // leftover item 6: on the DRAWN ground of its graded lot
                    // (its lowest at the walls), so no wall stands over air
                    float floorG = g;
                    if (CityHouses.PadsOn)
                    {
                        // its storeys on its own pad, its walls down to the
                        // lowest ground at them
                        floorG = CityHouses.Floor(map, b.pos, b.pos, rgt, b.w * 0.5f, b.d * 0.5f, out float lo);
                        y0 = lo - BuildingSink; y1 = floorG + b.h;
                    }
                    // a suburban house: the long side faces the road
                    EmitGableHouse(tm, b.pos, rgt, b.w * 0.5f, b.d * 0.5f, y0, floorG - BuildingSink + b.h * 0.7f, y1, Slot.FacadeHouse);
                    tm.houseSeats.Add(new HouseSeat { kind = 3, c = b.pos, u = rgt, hu = b.w * 0.5f, hv = b.d * 0.5f, y0 = y0, floor = floorG });
                    continue;
                }

                // Uptown B1: the fill boxes wear the facade atlas too (one
                // material with the OSM buildings round them, so a tile draws
                // no more facades than it did): a tall office box mostly
                // glass, the rest a midrise's mix, the brick boxes brick.
                // Retail (style 3) keeps its shopfront, the atlas above it.
                Slot wallSlot = Slot.FacadeGlass;
                bool shopFront = b.style == 3 && b.h > ShopFloorH + 1.5f;
                PickFacade(b.pos, (byte)(b.style == 0 ? (b.h > 40f ? 0 : 1) : b.style == 1 ? 1 : 2), 0, 0, default, g, b.h);
                // walls: front (facing road), right, back, left — outward normals
                EmitWallStyled(tm, c2, c1, y0, y1, fwd, b.style == 3, shopFront, wallSlot);
                EmitWallStyled(tm, c1, c4, y0, y1, rgt, b.style == 3, false, wallSlot);
                EmitWallStyled(tm, c4, c3, y0, y1, -fwd, b.style == 3, false, wallSlot);
                EmitWallStyled(tm, c3, c2, y0, y1, -rgt, b.style == 3, false, wallSlot);
                EndFacade();

                var roof = buckets[(int)Slot.RoofFlat];
                roof.Up(L(c2, y1, tm), L(c3, y1, tm), L(c4, y1, tm), L(c1, y1, tm),
                    new Vector2(c2.x / RoofFlatM, c2.y / RoofFlatM), new Vector2(c3.x / RoofFlatM, c3.y / RoofFlatM),
                    new Vector2(c4.x / RoofFlatM, c4.y / RoofFlatM), new Vector2(c1.x / RoofFlatM, c1.y / RoofFlatM));

            }
        }

        static Vector3 L(Vector2 p, float y, TileMeshes tm) =>
            new Vector3(p.x - tm.origin.x, y, p.y - tm.origin.z);

        /// <summary>One wall of a retail-or-not box: a shopfront on the
        /// ground floor where asked, brick above and elsewhere.</summary>
        static void EmitWallStyled(TileMeshes tm, Vector2 a, Vector2 c, float y0, float y1,
                                   Vector2 outward, bool retail, bool shopFront, Slot wallSlot)
        {
            float wallW = Vector2.Distance(a, c);
            if (retail)
            {
                float split = Mathf.Min(y0 + BuildingSink + ShopFloorH, y1);
                if (shopFront)
                {
                    float reps = Mathf.Max(1f, Mathf.Round(wallW / FacadeMeters[3].x));
                    EmitPanels(tm, Slot.Shops, a, c, y0 + BuildingSink, split, outward, reps, 1f);
                }
                else EmitFacadeQuad(tm, wallSlot, a, c, y0, split, outward);
                if (y1 > split + 0.2f) EmitFacadeQuad(tm, wallSlot, a, c, split, y1, outward);
                return;
            }
            EmitFacadeQuad(tm, wallSlot, a, c, y0, y1, outward);
        }

        static void EmitFacadeQuad(TileMeshes tm, Slot style, Vector2 a, Vector2 c,
                                   float y0, float y1, Vector2 outward)
        {
            if (style == Slot.FacadeGlass)
            {
                // the facade atlas: whole repeats along the wall, and V in
                // the building's own FLOORS from its ground floor up, so a
                // forty-storey tower shows forty bands (PickFacade)
                var lk = FacadeLooks[facLook];
                float wallM = Vector2.Distance(a, c);
                float ur = Mathf.Max(1f, Mathf.Round(wallM / lk.uM));
                float rep = facFloorH * lk.floors;
                float va = (y0 - facBase) / rep, vb = (y1 - facBase) / rep;
                EmitPanels(tm, style, a, c, y0, y1, outward, ur, vb - va, va);
                return;
            }
            var fm = FacadeMeters[(int)style - (int)Slot.FacadeTower];
            float wallW = Vector2.Distance(a, c);
            float u = Mathf.Max(1f, Mathf.Round(wallW / fm.x));
            float v = Mathf.Max(1f, Mathf.Round((y1 - y0) / fm.y));
            EmitPanels(tm, style, a, c, y0, y1, outward, u, v);
        }

        static int PanelCount(float meters) =>
            Mathf.Clamp(Mathf.CeilToInt(meters / FacadePanelMax), 1, meters > TallWallM ? TallPanelCap : FacadePanelCap);

        /// <summary>
        /// One wall, facing <paramref name="outward"/>, subdivided into
        /// affine-sized panels with the UV repeats handed out ACROSS the panels.
        /// The emitter checks its own work: if the first panel's normal points
        /// against the outward direction it was given, the tile counts a
        /// facing error and the audit fails the build. That is the whole
        /// history of the inside-out buildings, made into an assertion.
        /// </summary>
        static void EmitPanels(TileMeshes tm, Slot slot, Vector2 a, Vector2 c,
                               float y0, float y1, Vector2 outward, float uReps, float vReps, float vOff = 0f)
        {
            var bk = buckets[(int)slot];
            int nx = PanelCount(Vector2.Distance(a, c));
            int ny = PanelCount(y1 - y0);
            for (int j = 0; j < ny; j++)
            {
                float t0 = (float)j / ny, t1 = (float)(j + 1) / ny;
                float ya = Mathf.Lerp(y0, y1, t0), yb = Mathf.Lerp(y0, y1, t1);
                for (int i = 0; i < nx; i++)
                {
                    float s0 = (float)i / nx, s1 = (float)(i + 1) / nx;
                    Vector2 pa = Vector2.Lerp(a, c, s0), pc = Vector2.Lerp(a, c, s1);
                    bk.Wall(L(pa, 0f, tm), L(pc, 0f, tm), ya, yb, outward,
                            uReps * s0, uReps * s1, vOff + vReps * t0, vOff + vReps * t1);
                    if (i == 0 && j == 0)
                    {
                        var n = bk.LastNormal();
                        if (Vector3.Dot(n, new Vector3(outward.x, 0f, outward.y)) < 0f) tm.wallFacingErrors++;
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        //  Real buildings from OSM footprints
        // ------------------------------------------------------------------
        static readonly List<Vector2> roofScratch = new List<Vector2>(32);

        static IEnumerable<int> BuildFootprints(CityMap map, Trims trims, TileMeshes tm, int tx, int tz)
        {
            var list = map.FootprintsInTile(tx, tz);
            if (list == null) yield break;
            foreach (var fi in list)
            {
                yield return 0;   // WP-09: a footprint a step
                stepPhase = 7; stepItem = fi; stepPart = 0;
                var f = map.footprints[fi];
                if (f.propKind != 0) continue;   // a model stands here; CityWorld places it
                if (f.hidden) continue;          // Uptown B2: its building:parts draw it
                // B2: a part stands on its outline's ground and wears its
                // outline's look, so the tiers of one tower meet and match
                var gf = f.part && f.outline >= 0 && f.outline < map.footprints.Length ? map.footprints[f.outline] : f;
                float g = float.MaxValue;
                foreach (var p in f.pts) g = Mathf.Min(g, CityElevation.GroundY(map, p.x, p.y));
                if (gf != f) foreach (var p in gf.pts) g = Mathf.Min(g, CityElevation.GroundY(map, p.x, p.y));
                float y0 = g - BuildingSink;
                float top = y0 + BuildingSink + f.h;

                if (f.gable)
                {
                    var centre = f.centre; var u = f.u;
                    float hu = f.hu, hv = f.hv;
                    int fit = FitHouse(map, trims, ref centre, ref u, ref hu, ref hv, y0, top);
                    if (fit < 0) { tm.footprintsLeftOut++; continue; }
                    if (fit > 0) tm.footprintsCut++;
                    // leftover item 6: its storeys on its own graded pad, its
                    // walls down to the lowest ground at them
                    float floorG = y0 + BuildingSink;
                    if (CityHouses.PadsOn)
                    {
                        floorG = CityHouses.Floor(map, f.centre, centre, u, hu, hv, out float lo);
                        y0 = lo - BuildingSink; top = floorG + f.h;
                    }
                    float eave = floorG + f.h * 0.68f;
                    EmitGableHouse(tm, centre, u, hu, hv, y0, eave, top, Slot.FacadeHouse);
                    tm.houseSeats.Add(new HouseSeat { kind = 1, c = centre, u = u, hu = hu, hv = hv, y0 = y0, floor = floorG });
                    tm.footprintCount++;
                    continue;
                }
                fitPoly.Clear();
                fitPoly.AddRange(f.pts);
                int cut = FitFootprint(map, trims, fitPoly, y0, top);
                if (cut < 0) { tm.footprintsLeftOut++; continue; }
                if (cut > 0) tm.footprintsCut++;
                // leftover item 6: a house polygon on the drawn ground of its
                // graded lot (its box's lowest: never a wall over air)
                float floorP = y0 + BuildingSink;
                if (f.style == 3 && CityHouses.PadsOn)
                {
                    floorP = CityHouses.Floor(map, f.centre, f.centre, f.u, f.hu, f.hv, out float lo);
                    y0 = lo - BuildingSink; top = floorP + f.h;
                }

                // Uptown B1: towers, midrises and brick blocks all wear the
                // facade atlas (one material), each in its own look and tint
                // (shops: the shopfront strip on the street wall, the atlas
                // above it and round the other walls, brick or stone)
                Slot wallSlot = f.style == 3 ? Slot.FacadeHouse : Slot.FacadeGlass;
                if (wallSlot == Slot.FacadeGlass)
                    PickFacade(gf.centre, (byte)(f.style == 4 ? 2 : f.style), f.use, f.mat, f.colour, floorP,
                               gf != f ? Mathf.Max(gf.h, 1f) : top - floorP);
                // B2: a part's floor (a tier on its podium, a crown on its
                // shaft) and the eave its roof shape rises from
                bool floating = f.minH > 0.05f;
                float wallY0 = floating ? floorP + f.minH : y0;
                float eaveY = f.roof != 0 ? Mathf.Max(wallY0, top - f.roofH) : top;
                bool retail = f.style == 4 && !floating;
                // A shopfront goes on the wall that faces the nearest street.
                int frontWall = -1;
                if (retail && f.h > ShopFloorH + 1.5f &&
                    map.NearestRoadPoint(f.centre, 70f, skipLinks: true, out int rei, out float rs, out _))
                {
                    var q = map.edges[rei].PointAt(rs);
                    var toRoad = (q - f.centre).normalized;
                    float bestDot = 0.5f;
                    for (int i = 0; i < fitPoly.Count; i++)
                    {
                        var d = fitPoly[(i + 1) % fitPoly.Count] - fitPoly[i];
                        if (d.sqrMagnitude < 4f) continue;
                        var n = new Vector2(d.y, -d.x).normalized;   // outward, for a CCW polygon
                        float dt = Vector2.Dot(n, toRoad);
                        if (dt > bestDot) { bestDot = dt; frontWall = i; }
                    }
                }
                int n0 = fitPoly.Count;
                for (int i = 0; i < n0; i++)
                {
                    var a = fitPoly[i]; var c = fitPoly[(i + 1) % n0];
                    var d = c - a;
                    if (d.sqrMagnitude < 0.04f) continue;
                    var outward = new Vector2(d.y, -d.x).normalized;
                    if (eaveY > wallY0 + 0.05f)
                        EmitWallStyled(tm, a, c, wallY0, eaveY, outward, retail, i == frontWall, wallSlot);
                }

                roofScratch.Clear();
                roofScratch.AddRange(fitPoly);
                if (f.roof == 0) EarcutInto(buckets[(int)Slot.RoofFlat], roofScratch, top, tm.origin, RoofFlatM);
                else EmitRoofShape(tm, f, roofScratch, eaveY, top, wallSlot);
                if (floating && f.minH > 0.5f) EmitSoffit(tm, roofScratch, wallY0);

                // A tall tower gets a crown: a smaller prism on top, then a
                // smaller one still — enough silhouette to tell the tallest
                // towers from a box, at a distance, in fog.
                // (Not on one cut back off a street: it is centred on the
                // footprint's box, and would overhang the cut.)
                // (B2: not on a part, a shaped roof, or an outline its parts
                // rise from - their own tiers are the silhouette)
                if (f.h > 120f && cut == 0 && !f.part && f.roof == 0 && (f.landmark != 0 || !f.noSwap))
                    EmitCrown(map, tm, f, top, wallSlot);
                EndFacade();
                if (f.style == 3) tm.houseSeats.Add(new HouseSeat { kind = 2, c = f.centre, u = f.u, hu = f.hu, hv = f.hv, y0 = y0, floor = floorP });

                tm.footprintCount++;
            }
        }

        // ------------------------------------------------------------------
        //  Footprints off the pavement
        // ------------------------------------------------------------------
        /// <summary>
        /// How far a building's walls stand back from the pavement as the
        /// tiles DRAW it: the verge shoulder a car running off the edge rolls
        /// onto. An OSM footprint is traced to the back of a sidewalk, and the
        /// street beside it is drawn at its profile's width (a residential
        /// court is 7.9 m of tarmac), so facades stood up to 3.9 m into the
        /// lanes — and the buildings mesh is its own collider: a wall in the
        /// street. The lane survey found them on East 5th Street, Jacobs Lane,
        /// Gracie Way and South Tryon Street (2026-09-14); a census of every
        /// footprint put 370 of 31,700 on drawn pavement, 295 of them houses.
        /// </summary>
        const float FootprintClearM = VergeShoulderM;
        /// <summary>A building cut to less than this share of its plan area is
        /// left out: what would stand is a sliver of it.</summary>
        const float FootprintKeepShare = 0.5f;
        /// <summary>A house narrower or shallower than this after its cut is
        /// left out.</summary>
        const float HouseMinSideM = 3f;
        /// <summary>Cuts one footprint may take (a building along a bend meets
        /// one span's edge line after another) before it is left out.</summary>
        const int FootprintCutsMax = 12;
        static readonly List<Vector2> fitPoly = new List<Vector2>(32);
        static readonly List<Vector2> fitClip = new List<Vector2>(32);
        static readonly List<float> fitCross = new List<float>(8);
        static readonly Vector2[] fitPiece = new Vector2[4];

        /// <summary>
        /// Cut a footprint back until no pavement a car can reach at the
        /// building's height lies within <see cref="FootprintClearM"/> of its
        /// walls: each pass takes the piece it overlaps deepest and clips the
        /// polygon by that piece's edge line moved out by the clearance.
        /// Returns 0 untouched, the number of cuts, or -1 to leave it out (on
        /// the road itself, split in two, or a sliver).
        /// </summary>
        static int FitFootprint(CityMap map, Trims trims, List<Vector2> poly, float y0, float top)
        {
            float area0 = PolyArea(poly);
            if (area0 < 1f) return 0;
            for (int cuts = 0; ; cuts++)
            {
                if (!PavementCut(map, trims, poly, y0, top, out var linePt, out var lineOut)) return cuts;
                if (cuts >= FootprintCutsMax || !ClipPolyHalf(poly, linePt, lineOut, FootprintClearM)) return -1;
                if (PolyArea(poly) < area0 * FootprintKeepShare) return -1;
            }
        }

        /// <summary>
        /// The same for a gabled house, which is drawn on its oriented box:
        /// the box shrinks along whichever of its axes lies closer to the
        /// cut's normal, far enough that every corner clears the line, so it
        /// stays a box with a ridge. Returns as <see cref="FitFootprint"/>,
        /// with the box moved and resized in place.
        /// </summary>
        static int FitHouse(CityMap map, Trims trims, ref Vector2 centre, ref Vector2 u, ref float hu, ref float hv, float y0, float top)
        {
            var v = new Vector2(-u.y, u.x);
            float u0 = -hu, u1 = hu, v0 = -hv, v1 = hv;
            float area0 = 4f * hu * hv;
            for (int cuts = 0; ; cuts++)
            {
                fitPoly.Clear();
                fitPoly.Add(centre + u * u1 + v * v0); fitPoly.Add(centre + u * u1 + v * v1);
                fitPoly.Add(centre + u * u0 + v * v1); fitPoly.Add(centre + u * u0 + v * v0);
                if (!PavementCut(map, trims, fitPoly, y0, top, out var linePt, out var n))
                {
                    if (cuts == 0) return 0;
                    centre += u * ((u0 + u1) * 0.5f) + v * ((v0 + v1) * 0.5f);
                    hu = (u1 - u0) * 0.5f; hv = (v1 - v0) * 0.5f;
                    // the ridge runs along the long side, as it was built
                    if (hv > hu) { float t = hu; hu = hv; hv = t; u = v; }
                    return cuts;
                }
                if (cuts >= FootprintCutsMax) return -1;
                // every corner c + u a + v b must keep dot(. - linePt, n) >= clear
                float need = FootprintClearM - Vector2.Dot(centre - linePt, n);
                float nu = Vector2.Dot(u, n), nv = Vector2.Dot(v, n);
                if (Mathf.Abs(nu) >= Mathf.Abs(nv))
                {
                    float bound = (need - Mathf.Min(v0 * nv, v1 * nv)) / nu;
                    if (nu > 0f) u0 = Mathf.Max(u0, bound); else u1 = Mathf.Min(u1, bound);
                }
                else
                {
                    float bound = (need - Mathf.Min(u0 * nu, u1 * nu)) / nv;
                    if (nv > 0f) v0 = Mathf.Max(v0, bound); else v1 = Mathf.Min(v1, bound);
                }
                if (u1 - u0 < HouseMinSideM || v1 - v0 < HouseMinSideM || (u1 - u0) * (v1 - v0) < area0 * FootprintKeepShare) return -1;
            }
        }

        /// <summary>
        /// The deepest overlap between a plan polygon and the pavement round
        /// it, grown by <see cref="FootprintClearM"/>: every span of every
        /// ribbon as drawn (<see cref="OutlineOf"/>) and every junction fan
        /// triangle, that stands no higher than the roof and within a car's
        /// height of the base. False when nothing is that close; otherwise the
        /// line to cut along: a point on the piece's edge and its outward
        /// normal. The edge is the one the polygon reaches least far past —
        /// beside a street its side, beyond a dead end its end, on a fan its
        /// chord (a line through the node would cut into the fan's own
        /// pavement).
        /// </summary>
        static bool PavementCut(CityMap map, Trims trims, List<Vector2> poly, float y0, float top, out Vector2 linePt, out Vector2 lineOut)
        {
            linePt = default; lineOut = default;
            Vector2 bMin = poly[0], bMax = poly[0];
            foreach (var q in poly) { bMin = Vector2.Min(bMin, q); bMax = Vector2.Max(bMax, q); }
            GatherNear(map, bMin, bMax, FootprintClearM);
            GatherPavement(map, trims);
            float x0 = bMin.x - FootprintClearM, x1 = bMax.x + FootprintClearM;
            float z0 = bMin.y - FootprintClearM, z1 = bMax.y + FootprintClearM;
            float deepest = 0f;
            for (int k = 0; k < nearEdges.Count; k++)
            {
                var box = nearEdgeBox[k];
                if (x1 < box.x || x0 > box.z || z1 < box.y || z0 > box.w) continue;
                var ol = OutlineOf(map, trims, nearEdges[k]);
                if (ol.L == null || x1 < ol.minX || x0 > ol.maxX || z1 < ol.minZ || z0 > ol.maxZ) continue;
                for (int i = 1; i < ol.L.Length; i++)
                {
                    if (ol.BlockMisses(i, x0, z0, x1, z1)) { i += Outline.Block - 1; continue; }
                    Vector3 aL = ol.L[i - 1], bL = ol.L[i], bR = ol.R[i], aR = ol.R[i - 1];
                    if (x1 < Mathf.Min(Mathf.Min(aL.x, bL.x), Mathf.Min(bR.x, aR.x)) || x0 > Mathf.Max(Mathf.Max(aL.x, bL.x), Mathf.Max(bR.x, aR.x)) ||
                        z1 < Mathf.Min(Mathf.Min(aL.z, bL.z), Mathf.Min(bR.z, aR.z)) || z0 > Mathf.Max(Mathf.Max(aL.z, bL.z), Mathf.Max(bR.z, aR.z))) continue;
                    if (Mathf.Min(Mathf.Min(aL.y, bL.y), Mathf.Min(bR.y, aR.y)) > top ||
                        Mathf.Max(Mathf.Max(aL.y, bL.y), Mathf.Max(bR.y, aR.y)) + RoadsideRules.CarBandM < y0) continue;
                    fitPiece[0] = new Vector2(aL.x, aL.z); fitPiece[1] = new Vector2(bL.x, bL.z);
                    fitPiece[2] = new Vector2(bR.x, bR.z); fitPiece[3] = new Vector2(aR.x, aR.z);
                    if (Convex4(fitPiece))
                        PieceCut(poly, fitPiece, 4, 0xF, ref deepest, ref linePt, ref lineOut);
                    else
                    {
                        // a twisted span, as the ribbon draws it: its two
                        // triangles (aL, bL, bR) and (aL, bR, aR), whose
                        // shared diagonal is no edge of the pavement
                        PieceCut(poly, fitPiece, 3, 0x3, ref deepest, ref linePt, ref lineOut);
                        fitPiece[1] = new Vector2(bR.x, bR.z); fitPiece[2] = new Vector2(aR.x, aR.z);
                        PieceCut(poly, fitPiece, 3, 0x6, ref deepest, ref linePt, ref lineOut);
                    }
                }
            }
            foreach (int n in nearFans)
            {
                var fan = fanPolys[n];
                if (x1 < fan.centre.x - fan.reach || x0 > fan.centre.x + fan.reach || z1 < fan.centre.y - fan.reach || z0 > fan.centre.y + fan.reach) continue;
                var T = fan.tris;
                for (int i = 0; i + 2 < T.Length; i += 3)
                {
                    if (Mathf.Min(T[i].y, Mathf.Min(T[i + 1].y, T[i + 2].y)) > top ||
                        Mathf.Max(T[i].y, Mathf.Max(T[i + 1].y, T[i + 2].y)) + RoadsideRules.CarBandM < y0) continue;
                    int mask = 0x7;
                    for (int c = 0; c < 3; c++)
                        if (T[i + c].x == fan.centre.x && T[i + c].z == fan.centre.y) mask = 1 << ((c + 1) % 3);   // only the chord opposite the node
                    for (int c = 0; c < 3; c++) fitPiece[c] = new Vector2(T[i + c].x, T[i + c].z);
                    PieceCut(poly, fitPiece, 3, mask, ref deepest, ref linePt, ref lineOut);
                }
            }
            return deepest > 0f;
        }

        /// <summary>
        /// Does a plan polygon come within <see cref="FootprintClearM"/> of a
        /// convex piece (3 or 4 points, either winding)? If it does, the cut
        /// that clears it is the least, over the edges in
        /// <paramref name="edgeMask"/> (bit i = edge i to i + 1), of how far
        /// the polygon reaches back past that edge's line plus the clearance;
        /// kept when it is deeper than <paramref name="deepest"/>.
        /// </summary>
        static void PieceCut(List<Vector2> poly, Vector2[] piece, int count, int edgeMask, ref float deepest, ref Vector2 linePt, ref Vector2 lineOut)
        {
            float area = 0f;
            for (int j = 0; j < count; j++) area += Cross2(piece[j], piece[(j + 1) % count]);
            if (Mathf.Abs(area) < 0.02f) return;   // a collapsed wedge: its host's pavement is the pavement
            bool near = false;
            float clear2 = (FootprintClearM - 1e-3f) * (FootprintClearM - 1e-3f);
            for (int j = 0; j < count && !near; j++)
                if (PolyContains(poly, piece[j])) near = true;
            for (int i = 0; i < poly.Count && !near; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                bool inside = true;
                for (int j = 0; j < count; j++)
                {
                    Vector2 c = piece[j], d = piece[(j + 1) % count];
                    if (Cross2(d - c, a - c) * area < 0f) inside = false;
                    if (SegSegDist2(a, b, c, d) < clear2) { near = true; break; }
                }
                if (inside) near = true;
            }
            if (!near) return;
            float best = float.MaxValue; Vector2 bestPt = default, bestOut = default;
            for (int j = 0; j < count; j++)
            {
                if ((edgeMask & (1 << j)) == 0) continue;
                Vector2 c = piece[j], d = piece[(j + 1) % count];
                var dir = d - c;
                float len = dir.magnitude;
                if (len < 1e-3f) continue;
                // outward: the right of an anticlockwise edge
                var outw = (area > 0f ? new Vector2(dir.y, -dir.x) : new Vector2(-dir.y, dir.x)) / len;
                float reach = float.MaxValue;
                foreach (var q in poly) reach = Mathf.Min(reach, Vector2.Dot(q - c, outw));
                float cutDepth = FootprintClearM - reach;
                if (cutDepth < best) { best = cutDepth; bestPt = c; bestOut = outw; }
            }
            if (best == float.MaxValue || best <= deepest) return;
            deepest = best; linePt = bestPt; lineOut = bestOut;
        }

        /// <summary>Clip a polygon to the half-plane a clearance past a line
        /// (dot(p - linePt, outw) at least <paramref name="clear"/>). False
        /// where what is left is not one polygon. A footprint's outline may
        /// cross the line many times (a tower's stepped facade, a row of
        /// bays) and what is left is still one building as long as the runs
        /// of the line that close each removed part do not overlap; where
        /// they do, the line has cut the building in two, and the two would
        /// be joined by a wall of no thickness.</summary>
        static bool ClipPolyHalf(List<Vector2> poly, Vector2 linePt, Vector2 outw, float clear)
        {
            fitClip.Clear();
            fitCross.Clear();
            int n = poly.Count;
            var along = new Vector2(-outw.y, outw.x);
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % n];
                float da = Vector2.Dot(a - linePt, outw) - clear, db = Vector2.Dot(b - linePt, outw) - clear;
                if (da >= 0f) fitClip.Add(a);
                if ((da >= 0f) != (db >= 0f))
                {
                    var x = Vector2.Lerp(a, b, da / (da - db));
                    fitCross.Add(Vector2.Dot(x - linePt, along));
                    if (fitClip.Count == 0 || (fitClip[fitClip.Count - 1] - x).sqrMagnitude > 1e-4f) fitClip.Add(x);
                }
            }
            // crossings alternate leaving and re-entering the kept side; each
            // leave pairs with the re-entry after it, and the pair's run of
            // the line is where the new wall stands
            int c = fitCross.Count;
            if (c > 2)
            {
                bool leavesFirst = Vector2.Dot(poly[0] - linePt, outw) - clear >= 0f;
                for (int i = 0; i < c; i += 2)
                {
                    int i0 = leavesFirst ? i : (i + c - 1) % c, i1 = leavesFirst ? i + 1 : i;
                    float lo = Mathf.Min(fitCross[i0], fitCross[i1]), hi = Mathf.Max(fitCross[i0], fitCross[i1]);
                    for (int j = i + 2; j < c; j += 2)
                    {
                        int j0 = leavesFirst ? j : j - 1, j1 = leavesFirst ? j + 1 : j;
                        float lo2 = Mathf.Min(fitCross[j0], fitCross[j1]), hi2 = Mathf.Max(fitCross[j0], fitCross[j1]);
                        if (lo2 < hi - 1e-3f && hi2 > lo + 1e-3f) return false;
                    }
                }
            }
            if (fitClip.Count > 1 && (fitClip[0] - fitClip[fitClip.Count - 1]).sqrMagnitude <= 1e-4f) fitClip.RemoveAt(fitClip.Count - 1);
            if (fitClip.Count < 3) return false;
            poly.Clear();
            poly.AddRange(fitClip);
            return true;
        }

        static float PolyArea(List<Vector2> poly)
        {
            float a = 0f;
            for (int i = 0; i < poly.Count; i++) a += Cross2(poly[i], poly[(i + 1) % poly.Count]);
            return Mathf.Abs(a) * 0.5f;
        }

        static bool PolyContains(List<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        /// <summary>Is a four-point piece convex (its turns all one way)?</summary>
        static bool Convex4(Vector2[] q)
        {
            int pos = 0, neg = 0;
            for (int j = 0; j < 4; j++)
            {
                float c = Cross2(q[(j + 1) % 4] - q[j], q[(j + 2) % 4] - q[(j + 1) % 4]);
                if (c > 1e-6f) pos++; else if (c < -1e-6f) neg++;
            }
            return pos == 0 || neg == 0;
        }

        /// <summary>Squared plan distance between two segments.</summary>
        static float SegSegDist2(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float d1 = Cross2(d - c, a - c), d2 = Cross2(d - c, b - c), d3 = Cross2(b - a, c - a), d4 = Cross2(b - a, d - a);
            if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f))) return 0f;
            return Mathf.Min(Mathf.Min(PtSegDist2(a, c, d), PtSegDist2(b, c, d)), Mathf.Min(PtSegDist2(c, a, b), PtSegDist2(d, a, b)));
        }

        static float PtSegDist2(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-9f));
            return (a + ab * t - p).sqrMagnitude;
        }

        static void EmitCrown(CityMap map, TileMeshes tm, CityMap.Footprint f, float top, Slot slot)
        {
            float hu = f.hu, hv = f.hv;
            var v = new Vector2(-f.u.y, f.u.x);
            float y = top;
            for (int step = 0; step < 2; step++)
            {
                hu *= 0.62f; hv *= 0.62f;
                float h = f.h * (step == 0 ? 0.09f : 0.07f);
                var c1 = f.centre + f.u * hu + v * hv; var c2 = f.centre - f.u * hu + v * hv;
                var c3 = f.centre - f.u * hu - v * hv; var c4 = f.centre + f.u * hu - v * hv;
                EmitFacadeQuad(tm, slot, c2, c1, y, y + h, v);
                EmitFacadeQuad(tm, slot, c1, c4, y, y + h, f.u);
                EmitFacadeQuad(tm, slot, c4, c3, y, y + h, -v);
                EmitFacadeQuad(tm, slot, c3, c2, y, y + h, -f.u);
                var roof = buckets[(int)Slot.RoofFlat];
                roof.Up(L(c2, y + h, tm), L(c3, y + h, tm), L(c4, y + h, tm), L(c1, y + h, tm),
                    new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f));
                y += h;
            }
        }

        /// <summary>
        /// Uptown B2: an OSM roof:shape over a footprint's walls, simply, in
        /// the walls' own facade (a glass crown reads as glass): pyramidal to
        /// an apex over the centroid; dome in three rings to it; round a
        /// barrel, three rings to the long axis (its ends rounded); gabled a
        /// ridge along the long axis (ends hipped); skillion one plane falling
        /// along roof:direction (else across the short axis) over sloped
        /// wall tops, in the flat-roof material.
        /// </summary>
        static void EmitRoofShape(TileMeshes tm, CityMap.Footprint f, List<Vector2> poly, float eave, float top, Slot wallSlot)
        {
            int n = poly.Count;
            float rise = top - eave;
            if (n < 3) return;
            if (rise < 0.05f) { EarcutInto(buckets[(int)Slot.RoofFlat], poly, top, tm.origin, RoofFlatM); return; }
            var bk = buckets[(int)wallSlot];
            var lk = FacadeLooks[facLook];
            float rep = Mathf.Max(0.5f, facFloorH * lk.floors);
            if (f.roof == 4)
            {
                Vector2 dir = f.roofDir >= 0
                    ? new Vector2(Mathf.Sin(f.roofDir * Mathf.Deg2Rad), Mathf.Cos(f.roofDir * Mathf.Deg2Rad))
                    : new Vector2(f.u.y, -f.u.x);
                float s0 = float.MaxValue, s1 = float.MinValue;
                foreach (var p in poly) { float sp = Vector2.Dot(p, dir); s0 = Mathf.Min(s0, sp); s1 = Mathf.Max(s1, sp); }
                float span = Mathf.Max(0.5f, s1 - s0);
                float YAt(Vector2 p) => top - rise * (Vector2.Dot(p, dir) - s0) / span;
                for (int i = 0; i < n; i++)
                {
                    var a = poly[i]; var c = poly[(i + 1) % n];
                    var d = c - a;
                    if (d.sqrMagnitude < 0.04f) continue;
                    float ya = YAt(a), yc = YAt(c);
                    if (ya < eave + 0.02f && yc < eave + 0.02f) continue;
                    float ur = Mathf.Max(1f, Mathf.Round(d.magnitude / lk.uM));
                    bk.WallSloped(L(a, 0f, tm), L(c, 0f, tm), eave, ya, eave, yc, new Vector2(d.y, -d.x).normalized,
                                  0f, ur, (eave - facBase) / rep, (Mathf.Max(ya, yc) - facBase) / rep);
                }
                var rf = buckets[(int)Slot.RoofFlat];
                int v0 = rf.v.Count;
                EarcutInto(rf, poly, top, tm.origin, RoofFlatM);
                for (int k = v0; k < rf.v.Count; k++)
                {
                    var v = rf.v[k];
                    rf.v[k] = new Vector3(v.x, YAt(new Vector2(v.x + tm.origin.x, v.z + tm.origin.z)), v.z);
                }
                return;
            }
            // the rest: rings from the eave line to a point or a line
            Vector2 cen = Vector2.zero;
            {
                float ar = 0f;
                for (int i = 0; i < n; i++)
                {
                    var a = poly[i]; var b = poly[(i + 1) % n];
                    float cr = a.x * b.y - b.x * a.y;
                    ar += cr; cen += (a + b) * cr;
                }
                cen = Mathf.Abs(ar) > 1e-4f ? cen / (3f * ar) : poly[0];
            }
            bool toAxis = f.roof == 3 || f.roof == 5;
            int rings = f.roof == 2 || f.roof == 3 ? 3 : 1;
            float axisHalf = Mathf.Max(0f, f.hu - f.hv);
            Vector2 Target(Vector2 p) => toAxis
                ? f.centre + f.u * Mathf.Clamp(Vector2.Dot(p - f.centre, f.u), -axisHalf, axisHalf) : cen;
            for (int i = 0; i < n; i++)
            {
                var a0 = poly[i]; var b0 = poly[(i + 1) % n];
                var d = b0 - a0;
                if (d.sqrMagnitude < 0.04f) continue;
                var outw = new Vector2(d.y, -d.x).normalized;
                var facing = new Vector3(outw.x, 0.7f, outw.y);
                Vector2 ta = Target(a0), tb = Target(b0);
                float ur = Mathf.Max(1f, Mathf.Round(d.magnitude / lk.uM));
                for (int k = 1; k <= rings; k++)
                {
                    float th0 = (k - 1) * Mathf.PI * 0.5f / rings, th1 = k * Mathf.PI * 0.5f / rings;
                    float c0 = Mathf.Cos(th0), c1 = k == rings ? 0f : Mathf.Cos(th1);
                    float y0 = eave + rise * Mathf.Sin(th0), y1 = k == rings ? top : eave + rise * Mathf.Sin(th1);
                    var A = L(ta + (a0 - ta) * c0, y0, tm); var B = L(tb + (b0 - tb) * c0, y0, tm);
                    var C = L(tb + (b0 - tb) * c1, y1, tm); var D = L(ta + (a0 - ta) * c1, y1, tm);
                    float va = (y0 - facBase) / rep, vb = (y1 - facBase) / rep;
                    var uA = new Vector2(0f, va); var uB = new Vector2(ur, va);
                    var uC = new Vector2(ur * c1, vb); var uD = new Vector2(0f, vb);
                    if ((C - D).sqrMagnitude < 1e-4f)
                    {
                        // collapsed to a point: a triangle, facing out and up
                        if (Vector3.Dot(Vector3.Cross(C - A, B - A), facing) >= 0f) bk.Tri(A, B, C, uA, uB, uC);
                        else bk.Tri(A, C, B, uA, uC, uB);
                    }
                    else bk.Face(A, B, C, D, facing, uA, uB, uC, uD);
                }
            }
        }

        /// <summary>Uptown B2: the underside of a floating tier (a part with
        /// min_height), facing down, in the flat-roof material.</summary>
        static void EmitSoffit(TileMeshes tm, List<Vector2> poly, float y)
        {
            var rf = buckets[(int)Slot.RoofFlat];
            int t0 = rf.t.Count;
            EarcutInto(rf, poly, y, tm.origin, RoofFlatM);
            for (int j = t0; j + 2 < rf.t.Count; j += 3) { int x = rf.t[j + 1]; rf.t[j + 1] = rf.t[j + 2]; rf.t[j + 2] = x; }
        }

        /// <summary>
        /// A gabled house on an oriented box: four walls to the eave, two roof
        /// planes to a ridge along the long axis, a gable triangle at each end.
        /// The same routine builds a real footprint's house and the interior
        /// fill's, so they cannot look like two kinds of house.
        /// </summary>
        static void EmitGableHouse(TileMeshes tm, Vector2 centre, Vector2 u, float hu, float hv,
                                   float y0, float eave, float ridge, Slot wallSlot)
        {
            var v = new Vector2(-u.y, u.x);
            var c1 = centre + u * hu + v * hv;   // +u +v
            var c2 = centre - u * hu + v * hv;   // -u +v
            var c3 = centre - u * hu - v * hv;   // -u -v
            var c4 = centre + u * hu - v * hv;   // +u -v
            EmitFacadeQuad(tm, wallSlot, c2, c1, y0, eave, v);
            EmitFacadeQuad(tm, wallSlot, c1, c4, y0, eave, u);
            EmitFacadeQuad(tm, wallSlot, c4, c3, y0, eave, -v);
            EmitFacadeQuad(tm, wallSlot, c3, c2, y0, eave, -u);

            var rN = centre + u * hu;   // ridge ends
            var rS = centre - u * hu;
            var roof = buckets[(int)Slot.RoofTiles];
            float uRep = Mathf.Max(1f, Mathf.Round(hu * 2f / RoofTileM));
            float vRep = Mathf.Max(1f, Mathf.Round(Mathf.Sqrt(hv * hv + (ridge - eave) * (ridge - eave)) / RoofTileM));
            // +v plane and -v plane, both facing up (Up() settles the order)
            roof.Up(L(rS, ridge, tm), L(rN, ridge, tm), L(c1, eave, tm), L(c2, eave, tm),
                    new Vector2(0f, 0f), new Vector2(uRep, 0f), new Vector2(uRep, vRep), new Vector2(0f, vRep));
            roof.Up(L(c3, eave, tm), L(c4, eave, tm), L(rN, ridge, tm), L(rS, ridge, tm),
                    new Vector2(0f, vRep), new Vector2(uRep, vRep), new Vector2(uRep, 0f), new Vector2(0f, 0f));

            // gable ends: vertical triangles facing +u and -u
            var wall = buckets[(int)wallSlot];
            EmitGableTri(wall, tm, c1, rN, c4, eave, ridge, u);
            EmitGableTri(wall, tm, c3, rS, c2, eave, ridge, -u);

        }

        static void EmitGableTri(Bucket bk, TileMeshes tm, Vector2 a, Vector2 apex, Vector2 c,
                                 float eave, float ridge, Vector2 outward)
        {
            // a triangle from a to c faces the LEFT of a->c, like a wall
            Vector2 d = c - a;
            Vector2 left = new Vector2(-d.y, d.x);
            if (Vector2.Dot(left, outward) < 0f) { var t = a; a = c; c = t; }
            var fm = FacadeMeters[(int)Slot.FacadeHouse - (int)Slot.FacadeTower];
            float uRep = Mathf.Max(1f, Mathf.Round(Vector2.Distance(a, c) / fm.x));
            bk.Tri(L(a, eave, tm), L(apex, ridge, tm), L(c, eave, tm),
                   new Vector2(0f, 0f), new Vector2(uRep * 0.5f, (ridge - eave) / fm.y), new Vector2(uRep, 0f));
        }

        // ------------------------------------------------------------------
        //  Interior fill: houses between the arterials, where OSM gave us
        //  roads but no footprints. A deterministic grid per tile, aligned to
        //  the nearest street, thinning with distance from uptown and from the
        //  road. Cheap gable boxes in the tile mesh — one draw call for a
        //  whole subdivision.
        // ------------------------------------------------------------------
        const float HouseCell = 24f;

        static void BuildHouses(CityMap map, TileMeshes tm, int tx, int tz)
        {
            // leftover item 6: the placement lives in CityHouses (the same
            // choices, cell by cell), so the lots, the driveways and the
            // occupancy mask see the fill houses before any tile draws them
            foreach (var h in CityHouses.FillOf(map, tx, tz))
            {
                var c = h.c; var u = h.u; float hu = h.hu, hv = h.hv;
                var v = new Vector2(-u.y, u.x);
                float y0, floor;
                if (CityHouses.PadsOn)
                {
                    // its storeys on its own graded pad, its walls down to
                    // the lowest ground at them
                    floor = CityHouses.Floor(map, c, c, u, hu, hv, out float lo);
                    y0 = lo - BuildingSink;
                }
                else
                {
                    float g = float.MaxValue;
                    foreach (var corner in new[] { c + u * hu + v * hv, c - u * hu + v * hv, c - u * hu - v * hv, c + u * hu - v * hv })
                        g = Mathf.Min(g, CityElevation.GroundY(map, corner.x, corner.y));
                    y0 = g - BuildingSink; floor = g;
                }
                EmitGableHouse(tm, c, u, hu, hv, y0, floor + h.eaveH, floor + h.eaveH + h.riseH, Slot.FacadeHouse);
                tm.houseCount++;
                tm.houseBoxes.Add((c, u, hu, hv));
                tm.houseSeats.Add(new HouseSeat { kind = 4, c = c, u = u, hu = hu, hv = hv, y0 = y0, floor = floor });
            }
        }

        static float Hash01(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + salt * 2246822519) + 1442695041u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }
    }
}
