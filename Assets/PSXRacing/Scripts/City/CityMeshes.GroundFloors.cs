using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  THE UPTOWN GROUND FLOORS (2026-11-10; the owner: "reference road level
    //  photos to make the city accurate enough to be recognizable").
    //
    //  WHAT THE STREETS SHOW (28 block faces looked at at road level; the
    //  frames are Editor/CityRefSpots.Uptown.cs's spots): no Uptown building
    //  meets its sidewalk as the floors above it. A tower stands on square
    //  piers with a glazed lobby set back between them, a canopy over its
    //  door; a hotel adds planters and a blade sign; the low blocks of Tryon
    //  and Trade are shopfronts under awnings, a patio rail or a hedge box in
    //  front of some; theatres, museums and the backs of towers are blank
    //  precast or brick with a loading dock; and a parking deck is open
    //  spandrels and columns all the way up, a two-lane mouth with red
    //  bollards and a blade sign where its driveway meets the street. The
    //  game drew every one of them as its window pattern down to the ground.
    //
    //  THE RULE, keyed on what the generator knows:
    //    which walls   a wall of a building inside the freeway loop
    //                  (CityPoles.Uptown) that FACES A STREET (GfStreet: open
    //                  air in front of it, a street's pavement within reach
    //                  square to it, no other building between) takes a
    //                  ground-floor BAND from its floor to a storey line
    //                  (GfBegin: one storey, two under a 100 m tower, more
    //                  where the street falls away along it); the atlas wall
    //                  starts at that line - the band REPLACES the wall
    //                  there, never a decal on it.
    //    what it is    OpenStreetMap's tags (tools/city/groundfloors.py ->
    //                  CityGroundFloors.Data.cs): a PARKING deck, a HOTEL, a
    //                  place of WORSHIP, a CIVIC hall (museum, theatre,
    //                  arena, court, station), a SHOP or restaurant, HOMES;
    //                  untagged it is an office. With its height and its
    //                  street's class (Tryon or Trade Street: the dressed
    //                  main streets; the side streets) and its own hash:
    //                    DECK    every wall, every level: precast spandrels
    //                            proud of dark openings, columns between; its
    //                            entrance where OSM's driveway crosses its
    //                            outline (else a third along its front), a
    //                            mouth two lanes wide, five red bollards, a
    //                            clearance bar, a red blade sign (PARK), a
    //                            light under the soffit.
    //                    LOBBY   square piers a bay apart, a fascia over
    //                            them, glazing set back 1.2 m; on the
    //                            building's FRONT (its longest wall on its
    //                            best street) a canopy over the middle bay
    //                            and a downlight; a hotel's canopy takes two
    //                            white planters and a blade sign in a made-up
    //                            name.
    //                    ARCADE  the same, the glazing 3 m back behind free
    //                            piers.
    //                    SHOPS   shopfronts a bay each: glass over a stall
    //                            riser, a sign band (no lettering), an awning
    //                            in the bay's own colour on most; on a main
    //                            street's wide walk a patio rail or a hedge
    //                            planter in front of some.
    //                    BLANK   a solid base in the wall's own stone under a
    //                            string course, a loading dock or two (a dark
    //                            roll-up door set in) on a long wall.
    //                  towers front Tryon and Trade with lobbies and arcades,
    //                  the low blocks there with shops; on a side street a
    //                  building's front is a lobby and its other walls a mix
    //                  of blank, lobby and arcade; halls and churches are
    //                  blank with one glazed front.
    //    the places    GfPlaces: the landmarks the rule cannot know - a row
    //                  is a place (a point in the building) and the style its
    //                  street walls take. No names (Docs: NO BRANDS).
    //
    //  Everything rides the facade atlas's bucket (no material, no draw
    //  call): stone and metal are column 7's pack surfaces, the dark parts
    //  (openings, doors, bollards) the brick column's wall strip under a dark
    //  tint, the glazing the curtain-wall columns. Piers, risers, glazing,
    //  bollards and planters are solid as drawn (the buildings mesh is its
    //  own collider); canopies, awnings, signs and soffits are drawn only.
    //  PSX_CITY_GROUNDFLOORS=0: every wall as it was (the A/B).
    // ======================================================================
    public static partial class CityMeshes
    {
        /// <summary>PSX_CITY_GROUNDFLOORS=0: no ground-floor bands (the before). Not readonly: the reference shots photograph both.</summary>
        public static bool GroundFloorsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_GROUNDFLOORS") != "0";

        public const int GfOffice = 0, GfDeck = 1, GfHotel = 2, GfWorship = 3, GfCivic = 4, GfShop = 5, GfHomes = 6;
        public const int StNone = 0, StLobby = 1, StArcade = 2, StShops = 3, StBlank = 4, StDeck = 5;
        static readonly string[] GfStyleName = { "none", "lobby", "arcade", "shops", "blank", "deck" };

        /// <summary>What the last builds drew (the reference shots print it).</summary>
        public static class GroundStats
        {
            public static int buildings, walls, lobby, arcade, shops, blank, deckWalls, decks, deckDoors, deckDoorsOsm, canopies, planters, blades, docks, patios, hedges, solid;
            public static void Reset() { buildings = walls = lobby = arcade = shops = blank = deckWalls = decks = deckDoors = deckDoorsOsm = canopies = planters = blades = docks = patios = hedges = solid = 0; }
            public static string Line() =>
                $"ground floors: {walls} street walls of {buildings} buildings banded (lobby {lobby}, arcade {arcade}, shops {shops}, blank {blank}, short solid {solid}); " +
                $"{decks} parking decks ({deckWalls} walls) with {deckDoors} entrances ({deckDoorsOsm} where OSM's driveway crosses the outline); " +
                $"canopies {canopies}, planters {planters}, blade signs {blades}, loading docks {docks}, patio rails {patios}, hedge planters {hedges}";
        }

        // ------------------------------------------------------------------
        //  THE PLACES. A row: a point inside the building (the lat/lon a
        //  street-level frame was looked at from, moved ahead along its
        //  heading and to its right, metres), the style its street walls take
        //  and what was seen there. No names.
        // ------------------------------------------------------------------
        struct GfPlace
        {
            public string id, what; public Vector2 at; public int style; public Vector3 stone; public bool docks;
            public GfPlace(string id, double lat, double lon, float hdg, float ahead, float right, int style, Vector3 stone, bool docks, string what)
            {
                this.id = id; this.what = what; this.style = style; this.stone = stone; this.docks = docks;
                float hr = hdg * Mathf.Deg2Rad;
                var p = new Vector2((float)((lon + 80.81770185962013) * 111320.0 * System.Math.Cos(35.18456 * System.Math.PI / 180.0)),
                                    (float)((lat - 35.18456015184093) * 111132.0));
                at = p + new Vector2(Mathf.Sin(hr), Mathf.Cos(hr)) * ahead + new Vector2(Mathf.Cos(hr), -Mathf.Sin(hr)) * right;
            }
        }
        static readonly Vector3 StoneBuff = new Vector3(1.10f, 1.02f, 0.88f), StoneWhite = new Vector3(1.50f, 1.48f, 1.42f),
                                StoneGrey = new Vector3(0.90f, 0.90f, 0.92f), StoneRed = new Vector3(0.95f, 0.62f, 0.52f);
        static readonly GfPlace[] GfPlaces =
        {
            new GfPlace("e3rd_south_arcade", 35.22548, -80.84508, 135f, 45f, 24f, StArcade, StoneBuff, false, "East 3rd Street, south side: square buff piers, glazing set well back"),
            new GfPlace("scollege_white_podium", 35.22525, -80.84345, 230f, 45f, 26f, StBlank, StoneWhite, false, "South College Street, 4th to 3rd: a white concrete podium, strip windows over it"),
            new GfPlace("e5th_north_hall", 35.22765, -80.84150, 140f, 35f, -24f, StBlank, StoneBuff, true, "East 5th Street: blank precast and brick hall walls, loading docks"),
            new GfPlace("e5th_south_hall", 35.22765, -80.84150, 140f, 35f, 24f, StBlank, StoneRed, true, "East 5th Street, south side: tan and red panels, no openings"),
            new GfPlace("emlk_north_docks", 35.22455, -80.84595, 140f, 25f, -22f, StBlank, StoneGrey, true, "East MLK Boulevard: grey precast, a truck dock under the skybridge"),
            new GfPlace("emlk_south_docks", 35.22455, -80.84595, 140f, 25f, 22f, StBlank, StoneGrey, true, "East MLK Boulevard, south side: service doors and a dock"),
            new GfPlace("schurch_louvres", 35.22623, -80.84692, 50f, 40f, 24f, StBlank, StoneGrey, false, "South Church Street, MLK to 3rd: a tower's podium garage behind dark louvres"),
            new GfPlace("sbrevard_white_base", 35.22265, -80.84282, 50f, 25f, -26f, StBlank, StoneWhite, false, "South Brevard Street: a white stone tower base, no shopfronts"),
            new GfPlace("wtrade_round_podium", 35.22855, -80.84464, 320f, 30f, -28f, StLobby, StoneBuff, false, "West Trade Street, Church to Poplar: a two-storey glazed podium in buff stone"),
        };

        // ---- what a footprint is (the tags, the places, the deck doors), found once a map ----
        static CityMap gfDataMap;
        static Dictionary<int, byte> gfKindOf;
        static Dictionary<int, int> gfPlaceOf;
        static Dictionary<int, List<Vector2>> gfDoorsOf;
        static int[] gfPlaceFoot;

        /// <summary>The building a footprint belongs to: its outline's index, or its own.</summary>
        static int GfBuilding(CityMap map, int fi)
        {
            var f = map.footprints[fi];
            return f.part && f.outline >= 0 && f.outline < map.footprints.Length ? f.outline : fi;
        }

        /// <summary>The building whose plan holds p (an outline before a part), or the nearest within <paramref name="reach"/>; -1 none.</summary>
        static int GfBuildingAt(CityMap map, Vector2 p, float reach)
        {
            int tx = Mathf.FloorToInt(p.x / TileSize), tz = Mathf.FloorToInt(p.y / TileSize);
            int best = -1, bestRank = 99; float bestD = float.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var list = map.FootprintsInTile(tx + dx, tz + dz);
                    if (list == null) continue;
                    foreach (int fi in list)
                    {
                        var f = map.footprints[fi];
                        if (f.pts == null || f.pts.Length < 3 || f.replaced || f.propKind != 0 || f.gable || f.style == 3) continue;
                        float d = 0f;
                        if (!SkyInPoly(f.pts, p))
                        {
                            d = float.MaxValue;
                            for (int i = 0, j = f.pts.Length - 1; i < f.pts.Length; j = i++)
                                d = Mathf.Min(d, RoadsideOccupancy.DistToSeg(p, f.pts[j], f.pts[i]));
                            if (d > reach) continue;
                        }
                        int rank = d <= 0f ? (f.part ? 1 : 0) : 2;
                        if (rank < bestRank || (rank == bestRank && d < bestD)) { bestRank = rank; bestD = d; best = GfBuilding(map, fi); }
                    }
                }
            return best;
        }

        static void GfData(CityMap map)
        {
            if (gfDataMap == map && gfKindOf != null) return;
            gfDataMap = map;
            gfKindOf = new Dictionary<int, byte>();
            gfPlaceOf = new Dictionary<int, int>();
            gfDoorsOf = new Dictionary<int, List<Vector2>>();
            for (int i = 0; i + 2 < GfTagged.Length; i += 3)
            {
                int b = GfBuildingAt(map, new Vector2(GfTagged[i], GfTagged[i + 1]), 0f);
                if (b >= 0 && !gfKindOf.ContainsKey(b)) gfKindOf[b] = (byte)GfTagged[i + 2];
            }
            gfPlaceFoot = new int[GfPlaces.Length];
            for (int i = 0; i < GfPlaces.Length; i++)
            {
                int b = GfBuildingAt(map, GfPlaces[i].at, 14f);
                gfPlaceFoot[i] = b;
                if (b >= 0 && !gfPlaceOf.ContainsKey(b)) gfPlaceOf[b] = i;
            }
            for (int i = 0; i + 1 < GfDeckDoors.Length; i += 2)
            {
                var p = new Vector2(GfDeckDoors[i], GfDeckDoors[i + 1]);
                int b = GfBuildingAt(map, p, 3f);
                if (b < 0 || !gfKindOf.TryGetValue(b, out byte k) || k != GfDeck) continue;
                if (!gfDoorsOf.TryGetValue(b, out var l)) { l = new List<Vector2>(2); gfDoorsOf[b] = l; }
                l.Add(p);
            }
        }

        /// <summary>One line a place: the building it found, or none (the hand-back's list).</summary>
        public static string GroundFloorReport(CityMap map)
        {
            GfData(map);
            var sb = new System.Text.StringBuilder();
            int found = 0, tagged = 0;
            foreach (var kv in gfKindOf) if (CityPoles.Uptown(map, map.footprints[kv.Key].centre)) tagged++;
            for (int i = 0; i < GfPlaces.Length; i++)
            {
                int b = gfPlaceFoot[i];
                if (b >= 0) found++;
                sb.Append($"  {GfPlaces[i].id} ({GfStyleName[GfPlaces[i].style]}; {GfPlaces[i].what}): {(b >= 0 ? $"footprint {b}, {map.footprints[b].h:0} m" : "NO BUILDING within 14 m of its point")}\n");
            }
            return $"ground-floor data: {gfKindOf.Count} of {GfTagged.Length / 3} tagged buildings matched a footprint ({tagged} inside the loop), {gfDoorsOf.Count} decks with a mapped driveway; places: {found} of {GfPlaces.Length} found\n" + sb;
        }

        // ---- the building being banded (GfBegin .. EndFacade) ----
        static bool gfActive;
        static CityMap gfMap; static Trims gfTrims; static TileMeshes gfTm;
        static int gfKind, gfPlace, gfBid;
        static float gfFloor, gfY0, gfSplit, gfTop;
        static uint gfHash;
        static Vector2 gfFrontMid, gfDoorAt;
        static bool gfHasFront, gfHasDoor, gfTall, gfMainFront;
        static readonly List<Vector2> gfPoly = new List<Vector2>(32);
        // the wall being banded
        static Vector2 gfA, gfD, gfN;
        static float gfLen, gfOff, gfRoadY0, gfRoadY1, gfRoadYm;
        static int gfDress;
        static Bucket GfBk => buckets[(int)Slot.FacadeGlass];

        static Vector3 GP(float s, float o, float y) => L(gfA + gfD * s + gfN * o, y, gfTm);
        static Vector2 GW(float s, float o) => gfA + gfD * s + gfN * o;

        /// <summary>Does this wall face a street? Its nearest street's edge, how far the wall stands off that road's model and the street's dress.</summary>
        static bool GfStreet(Vector2 a, Vector2 c, Vector2 n, out int edge, out float arc, out float off, out int dress)
        {
            edge = -1; arc = 0f; off = 0f; dress = DressNone;
            var mid = (a + c) * 0.5f;
            if (!gfMap.FootprintClear(mid + n * 1.5f, 0.6f)) return false;
            if (!gfMap.NearestRoadPoint(mid + n * 2f, 30f, true, out edge, out arc, out _)) return false;
            var e = gfMap.edges[edge];
            if (e.tunnel || e.bridge || e.cls >= 5 || e.ElevatedAt(arc)) return false;
            var to = e.PointAt(arc) - mid;
            float sq = Vector2.Dot(to, n);
            if (sq < 1.5f || sq < 0.72f * to.magnitude) return false;
            off = OffRoadModel(e, gfTrims, arc, mid);
            if (off > 20f || off < 0.3f) return false;
            // nothing else stands between the wall and the street
            if (off > 5f && (!gfMap.FootprintClear(mid + n * (off * 0.5f), 0.5f) || !gfMap.FootprintClear(mid + n * (off - 1.5f), 0.5f))) return false;
            dress = StreetDressOf(gfMap, e);
            return true;
        }

        /// <summary>
        /// Decide a footprint's ground floor (EmitFootprint, after PickFacade): false leaves its walls as they were.
        /// <paramref name="poly"/> its walls as drawn, <paramref name="y0"/> their sunk foot, <paramref name="floor"/> its floor,
        /// <paramref name="top"/> its walls' top.
        /// </summary>
        static bool GfBegin(CityMap map, Trims trims, TileMeshes tm, int fi, CityMap.Footprint f, CityMap.Footprint gf, List<Vector2> poly, float y0, float floor, float top)
        {
            gfActive = false;
            if (!GroundFloorsOn || skylineBuild || proxyBuild || poly.Count < 3 || top - floor < 5.5f) return false;
            if (!CityPoles.Uptown(map, gf.centre)) return false;
            GfData(map);
            gfMap = map; gfTrims = trims; gfTm = tm;
            gfBid = GfBuilding(map, fi);
            gfKind = gfKindOf.TryGetValue(gfBid, out byte k) ? k : GfOffice;
            gfPlace = gfPlaceOf.TryGetValue(gfBid, out int pl) ? pl : -1;
            gfHash = FacadeHash(gf.centre + new Vector2(-4.4f, 9.1f));
            gfFloor = floor; gfY0 = y0; gfTop = top;
            gfTall = Mathf.Max(top - floor, gf.h) >= 30f;
            gfPoly.Clear(); gfPoly.AddRange(poly);
            // the street walls: the front (the longest on the best street) and the highest ground along them
            float best = 0f, maxG = floor;
            gfHasFront = false; gfMainFront = false;
            int n0 = poly.Count, streetWalls = 0;
            for (int i = 0; i < n0; i++)
            {
                Vector2 a = poly[i], c = poly[(i + 1) % n0], d = c - a;
                float len = d.magnitude;
                if (len < 2.5f) continue;
                var n = new Vector2(d.y, -d.x) / len;
                if (!GfStreet(a, c, n, out int ei, out float arc, out float off, out int dress)) continue;
                streetWalls++;
                for (int q = 0; q <= 2; q++)
                {
                    var p = Vector2.Lerp(a, c, q * 0.5f) + n * 1.0f;
                    maxG = Mathf.Max(maxG, CityElevation.GroundY(map, p.x, p.y));
                }
                maxG = Mathf.Max(maxG, map.edges[ei].YAt(arc) + 0.28f);
                float score = len * (dress == DressMain ? 1.6f : 1f) * (off < 9f ? 1f : 0.6f);
                if (score > best) { best = score; gfFrontMid = (a + c) * 0.5f; gfHasFront = true; gfMainFront = dress == DressMain; }
            }
            if (gfKind != GfDeck && streetWalls == 0) return false;
            // the band's top: a storey line, the lobby's own (one, two under a tall tower), clear of the highest ground
            int ns = Mathf.Max(1, facBaseN);
            while (floor + ns * facFloorH - maxG < 3.0f && ns < 4) ns++;
            gfSplit = floor + ns * facFloorH;
            if (gfKind != GfDeck && top - gfSplit < 2.0f) return false;
            // a deck's entrance: OSM's driveway, else a third along its front
            gfHasDoor = false;
            if (gfKind == GfDeck)
            {
                if (gfDoorsOf.TryGetValue(gfBid, out var doors))
                {
                    // the door nearest this footprint's own walls (a deck drawn by parts: the part it is in)
                    float bd = 4f;
                    foreach (var q in doors)
                        for (int i = 0; i < n0; i++)
                        {
                            float dd = RoadsideOccupancy.DistToSeg(q, poly[i], poly[(i + 1) % n0]);
                            if (dd < bd) { bd = dd; gfDoorAt = q; gfHasDoor = true; }
                        }
                    if (gfHasDoor) GroundStats.deckDoorsOsm++;
                }
                if (!gfHasDoor && gfHasFront && !f.part) { gfDoorAt = gfFrontMid; gfHasDoor = true; }
                GroundStats.decks++;
            }
            GroundStats.buildings++;
            gfActive = true;
            return true;
        }

        /// <summary>The wall's colour as a tint of column 7's stone (its own albedo), or a stone by the building's hash under glass.</summary>
        static Vector3 GfStoneOf()
        {
            if (gfPlace >= 0) return GfPlaces[gfPlace].stone;
            var t = Bucket.Tint;
            if (facLook <= LookTeal)
            {
                int p = (int)((gfHash >> 6) % 5u);
                return p == 0 ? new Vector3(0.62f, 0.62f, 0.66f) : p == 1 ? StoneBuff : p == 2 ? StoneGrey : p == 3 ? new Vector3(0.80f, 0.60f, 0.54f) : new Vector3(1.30f, 1.28f, 1.22f);
            }
            var kWall = new Vector3(t.r & 0xFE, t.g, t.b) / 127.5f;
            var k = Vector3.Scale(kWall, FacadeLooks[facLook].meanLin) * 2.0f;
            float m = Mathf.Max(k.x, Mathf.Max(k.y, k.z));
            return m > 1.9f ? k * (1.9f / m) : k;
        }

        static void GfStone(Vector3 k) => PartsTint(k, false);
        static void GfMetal(Vector3 k) => PartsTint(k, true);
        /// <summary>The dark matte surface: the brick column's wall-only strip (GfDarkV) under a dark tint - openings, doors, bollards, rails.</summary>
        static void GfDark(Vector3 k)
        {
            Bucket.Tint = new Color32(TintR(k.x, LookBrick), TintByte(k.y), TintByte(k.z), (byte)(LookBrick * 32));
            Bucket.FacOn = false;
        }
        static readonly Vector3 DarkGrey = new Vector3(0.36f, 0.80f, 1.12f), DarkBlack = new Vector3(0.36f, 0.62f, 0.88f),
                                DarkRed = new Vector3(1.99f, 0.48f, 0.46f), DarkDoor = new Vector3(0.80f, 1.60f, 1.99f), DarkBronze = new Vector3(0.70f, 0.95f, 0.80f),
                                DarkLeaf = new Vector3(0.36f, 0.90f, 0.46f);   // a clipped shrub's dark green (the tint multiplies the texel as stored)
        const float GfDarkU0 = 0.474f, GfDarkU1 = 0.487f, GfDarkV0 = 0.771f, GfDarkV1 = 0.779f;
        static bool gfDarkUv;

        /// <summary>A vertical quad parallel to the wall, <paramref name="o"/> out of it (negative: set back), facing the street (or the building: back).</summary>
        static void GfFront(float s0, float s1, float o, float y0, float y1, bool back = false)
        {
            if (s1 - s0 < 0.02f || y1 - y0 < 0.02f) return;
            GfUv(s0, s1, y0, y1, out float u0, out float u1, out float v0, out float v1);
            GfBk.Wall(GP(s0, o, 0f), GP(s1, o, 0f), y0, y1, back ? -gfN : gfN, u0, u1, v0, v1);
        }
        /// <summary>A vertical quad square to the wall at <paramref name="s"/>, from o0 to o1, facing along the wall (+1) or back along it (-1).</summary>
        static void GfSide(float s, float o0, float o1, float y0, float y1, int facing)
        {
            if (Mathf.Abs(o1 - o0) < 0.02f || y1 - y0 < 0.02f) return;
            GfUv(o0, o1, y0, y1, out float u0, out float u1, out float v0, out float v1);
            GfBk.Wall(GP(s, o0, 0f), GP(s, o1, 0f), y0, y1, gfD * facing, u0, u1, v0, v1);
        }
        /// <summary>A level quad at y over [s0,s1] x [o0,o1], facing up or down.</summary>
        static void GfFlat(float s0, float s1, float o0, float o1, float y, bool up)
        {
            if (s1 - s0 < 0.02f || Mathf.Abs(o1 - o0) < 0.02f) return;
            GfUv(s0, s1, o0, o1, out float u0, out float u1, out float v0, out float v1);
            Vector3 a = GP(s0, o0, y), b = GP(s1, o0, y), c = GP(s1, o1, y), d = GP(s0, o1, y);
            if (up) GfBk.Up(a, b, c, d, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
            else GfBk.Down(a, b, c, d, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
        }
        static void GfUv(float a0, float a1, float b0, float b1, out float u0, out float u1, out float v0, out float v1)
        {
            // (a few texels inside one brick of the strip: a flat dark colour, no courses showing on a bollard)
            if (gfDarkUv) { u0 = GfDarkU0; u1 = GfDarkU1; v0 = GfDarkV0; v1 = GfDarkV1; return; }
            u0 = a0 * 0.5f; u1 = a1 * 0.5f; v0 = b0 / 8f; v1 = b1 / 8f;
        }
        /// <summary>A box in the wall's frame: faces 1 front, 2 back, 4 start side, 8 end side, 16 top, 32 underside.</summary>
        static void GfBox(float s0, float s1, float o0, float o1, float y0, float y1, int faces)
        {
            if ((faces & 1) != 0) GfFront(s0, s1, Mathf.Max(o0, o1), y0, y1);
            if ((faces & 2) != 0) GfFront(s0, s1, Mathf.Min(o0, o1), y0, y1, true);
            if ((faces & 4) != 0) GfSide(s0, o0, o1, y0, y1, -1);
            if ((faces & 8) != 0) GfSide(s1, o0, o1, y0, y1, 1);
            if ((faces & 16) != 0) GfFlat(s0, s1, o0, o1, y1, true);
            if ((faces & 32) != 0) GfFlat(s0, s1, o0, o1, y0, false);
        }

        /// <summary>Glazing parallel to the wall: a curtain-wall column, <paramref name="rows"/> of its floors up the pane, from the column's floor 1 (lit as an office floor).</summary>
        static void GfGlass(float s0, float s1, float o, float y0, float y1, int look, Vector3 k, float rows, float bayM)
        {
            if (s1 - s0 < 0.05f || y1 - y0 < 0.05f) return;
            var keep = Bucket.Tint;
            Bucket.Tint = new Color32(TintR(k.x, look), TintByte(k.y), TintByte(k.z), (byte)((look & 7) * 32));
            Bucket.FacOn = false;
            var lk = FacadeLooks[look];
            // whole mullion bays across the pane (the column's 8 bays a repeat)
            float bays = Mathf.Max(1f, Mathf.Round((s1 - s0) / bayM));
            float ub = bays / 8f, fl = 1f / lk.floors;
            GfBk.Wall(GP(s0, o, 0f), GP(s1, o, 0f), y0, y1, gfN, 0f, ub, fl, fl * (1f + rows));
            Bucket.Tint = keep;
        }

        /// <summary>The floor of a bay: the ground at the wall there and behind it, or the walk's back edge - whichever is higher.</summary>
        static float GfFloorAt(float s, float back)
        {
            var p = GW(s, 1.0f); var q = GW(s, -back);
            float g = Mathf.Max(CityElevation.GroundY(gfMap, p.x, p.y), CityElevation.GroundY(gfMap, q.x, q.y)) + 0.05f;
            float t = gfLen > 0.01f ? s / gfLen : 0f;
            float road = t < 0.5f ? Mathf.Lerp(gfRoadY0, gfRoadYm, t * 2f) : Mathf.Lerp(gfRoadYm, gfRoadY1, t * 2f - 1f);
            // (a clear step over the walk's back edge - about road + 0.13 - never a floor level with the slab that may run in under it)
            return Mathf.Min(Mathf.Max(g, road + 0.28f), gfSplit - 2.4f);
        }

        static float GfRoadY(Vector2 p, float fallback)
        {
            return gfMap.NearestRoadPoint(p, 30f, true, out int ei, out float arc, out _) ? gfMap.edges[ei].YAt(arc) : fallback;
        }

        static bool GfInside(float s, float o) => PolyContains(gfPoly, GW(s, o));

        /// <summary>
        /// One wall of the building GfBegin decided: its ground-floor band (or, a deck, the whole wall), drawn; returns the height
        /// the wall's atlas starts at (<paramref name="y0"/>: untouched; <paramref name="y1"/>: the wall is all the band's).
        /// </summary>
        static float GroundBand(TileMeshes tm, Vector2 a, Vector2 c, float y0, float y1, Vector2 n)
        {
            if (!gfActive) return y0;
            float len = Vector2.Distance(a, c);
            if (len < 0.3f) return y0;
            gfTm = tm; gfA = a; gfD = (c - a) / len; gfN = n; gfLen = len;
            var keep = Bucket.Tint; bool keepOn = Bucket.FacOn, keepNc = Bucket.NoCollide; int keepLook = facLook;
            float start = y0;
            try
            {
                if (gfKind == GfDeck) { GfDeckWall(y0, y1); start = y1; return start; }
                if (y1 < gfSplit + 1.5f) return y0;
                var mid = (a + c) * 0.5f;
                if (len < 2.5f)
                {
                    // a jog in a street front: a solid base, so the band's line carries round it
                    if (!gfMap.FootprintClear(mid + n * 1.5f, 0.6f)) return y0;
                    gfDarkUv = false; GfStone(GfStoneOf() * 0.92f);
                    GfFront(0f, len, 0f, y0, gfSplit);
                    GroundStats.solid++;
                    return start = gfSplit;
                }
                if (!GfStreet(a, c, n, out int ei, out float arc, out gfOff, out gfDress)) return y0;
                var e = gfMap.edges[ei];
                gfRoadYm = e.YAt(arc);
                gfRoadY0 = GfRoadY(a + n * 2f, gfRoadYm); gfRoadY1 = GfRoadY(c + n * 2f, gfRoadYm);
                bool front = gfHasFront && (mid - gfFrontMid).sqrMagnitude < 1.0f;
                uint h = FacadeHash(mid + new Vector2(2.7f, -6.3f));
                int style = GfStyleFor(len, front, h);
                GroundStats.walls++;
                switch (style)
                {
                    case StShops: GfShops(y0, h); GroundStats.shops++; break;
                    case StBlank: GfBlank(y0, h, front); GroundStats.blank++; break;
                    case StArcade: GfLobby(y0, h, front, true); GroundStats.arcade++; break;
                    default: GfLobby(y0, h, front, false); GroundStats.lobby++; break;
                }
                return start = gfSplit;
            }
            finally
            {
                Bucket.Tint = keep; Bucket.FacOn = keepOn; Bucket.NoCollide = keepNc; facLook = keepLook; gfDarkUv = false;
            }
        }

        /// <summary>The rule (the header): this wall's style.</summary>
        static int GfStyleFor(float len, bool front, uint h)
        {
            int p = (int)((h >> 5) % 100u);
            bool main = gfDress == DressMain;
            if (gfPlace >= 0)
            {
                int ps = GfPlaces[gfPlace].style;
                return ps == StBlank && front && !GfPlaces[gfPlace].docks && gfKind == GfHotel ? StLobby : ps;
            }
            switch (gfKind)
            {
                case GfHotel: return front || main ? StLobby : p < 45 ? StBlank : StLobby;
                case GfWorship: return StBlank;
                case GfCivic: return front ? StLobby : StBlank;
                case GfShop: return StShops;
                case GfHomes: return main ? StShops : front ? StLobby : p < 55 ? StBlank : StShops;
            }
            if (gfTall)
            {
                if (main) return p < 30 ? StArcade : p < 78 ? StLobby : StShops;
                if (front) return p < 25 ? StArcade : StLobby;
                return p < 42 ? StBlank : p < 62 ? StArcade : StLobby;
            }
            if (main) return StShops;
            bool masonry = IsPlainLook(facLook);
            return front ? (masonry ? StShops : StLobby) : p < (masonry ? 45 : 55) ? StBlank : masonry ? StShops : StLobby;
        }

        // ------------------------------------------------------------------
        //  LOBBY and ARCADE
        // ------------------------------------------------------------------
        static void GfLobby(float y0, uint h, bool front, bool arcade)
        {
            float len = gfLen;
            float pierW = arcade ? 1.0f : 0.8f;
            float recess = arcade ? 3.0f : 1.2f;
            recess = Mathf.Min(recess, (len - 2.0f - pierW) * 0.5f);
            var stone = GfStoneOf();
            gfDarkUv = false;
            if (recess < 0.3f) { GfStone(stone * 0.92f); GfFront(0f, len, 0f, y0, gfSplit); return; }
            float endW = recess + pierW * 0.5f;                    // a corner block: two walls' recesses never meet
            float bayM = 6.4f + 2.0f * (((gfHash >> 9) & 0xFF) / 255f);
            int nb = Mathf.Max(1, Mathf.RoundToInt((len - 2f * endW + pierW) / bayM));
            float bw = (len - 2f * endW + pierW) / nb;
            if (bw < pierW + 1.6f) { nb = 1; bw = len - 2f * endW + pierW; }
            float head = arcade ? 0.45f : 0.60f, yH = gfSplit - head;
            // the fascia over the piers, flush with the wall above
            GfStone(stone);
            GfFront(0f, len, 0f, yH, gfSplit);
            // the glazing: the tower's own glass under a glass tower, a dark or bronze glass under stone
            var wallTint = Bucket.Tint;
            int gLook; Vector3 gK;
            if (facLook <= LookTeal) { gLook = facLook; gK = new Vector3(1.05f, 1.10f, 1.18f); }
            else if (((gfHash >> 3) & 3u) == 0u) { gLook = LookTeal; gK = new Vector3(1.50f, 1.25f, 0.95f); }
            else { gLook = LookBlue; gK = new Vector3(1.20f, 1.32f, 1.50f); }
            float rows = gfSplit - gfFloor > 5.5f ? 2f : 1f;
            int doorBay = front ? nb / 2 : -1;
            for (int b = 0; b <= nb; b++)
            {
                // pier b stands at the left of bay b (the two end blocks reach the corners)
                float c0 = endW - pierW * 0.5f + b * bw;
                float s0 = b == 0 ? 0f : c0 - pierW * 0.5f, s1 = b == nb ? len : c0 + pierW * 0.5f;
                GfStone(stone);
                GfFront(s0, s1, 0f, y0, yH);
                bool end = b == 0 || b == nb;
                float deep = arcade && !end ? pierW : recess;
                if (b > 0) GfSide(s0, 0f, -deep, y0, yH, -1);
                if (b < nb) GfSide(s1, 0f, -deep, y0, yH, 1);
                if (arcade && !end) GfFront(s0, s1, -deep, y0, yH, true);
                if (b == nb) break;
                // bay b: s1 .. the next pier
                float e1 = b + 1 == nb ? len - endW : c0 + bw - pierW * 0.5f;
                float a0 = arcade ? (b == 0 ? s1 : s1 - pierW) : s1, a1 = arcade ? (b + 1 == nb ? e1 : e1 + pierW) : e1;
                float sm = (s1 + e1) * 0.5f;
                bool open = GfInside(sm, -recess - 0.4f);
                if (!open) { GfFront(s1, e1, 0f, y0, yH); continue; }
                float gy = GfFloorAt(sm, recess);
                GfFront(s1, e1, 0f, y0, gy);                              // the step at the wall line
                GfStone(stone * 0.80f);
                GfFlat(arcade ? a0 : s1, arcade ? a1 : e1, 0f, -recess, gy, true);   // the lobby's paving
                // (an arcade's glazing runs behind its free piers)
                GfGlass(a0, a1, -recess, gy, yH, gLook, gK, rows, gLook == LookSilver ? 1.9f : 1.6f);
                Bucket.NoCollide = true;
                GfStone(new Vector3(1.25f, 1.24f, 1.20f));
                GfFlat(arcade ? a0 : s1, arcade ? a1 : e1, 0f, -recess, yH, false);  // the soffit
                Bucket.NoCollide = false;
                if (b == doorBay) GfEntrance(s1, e1, gy, yH, recess, h);
            }
            Bucket.Tint = wallTint;
        }

        /// <summary>The front door's bay: a canopy over it, a downlight under that, a darker door pane; a hotel's planters and blade sign.</summary>
        static void GfEntrance(float s0, float s1, float gy, float yH, float recess, uint h)
        {
            float reach = Mathf.Min(2.4f, gfOff - 1.9f);
            float cy = Mathf.Min(gy + 3.1f, yH - 0.05f);
            Bucket.NoCollide = true;
            gfDarkUv = true; GfDark(DarkGrey);
            // the doors: a darker frame in the glazing's plane, a hand in front of it
            float dm = (s0 + s1) * 0.5f, dw = Mathf.Min(1.8f, (s1 - s0) * 0.5f - 0.2f);
            if (dw > 0.5f) { GfBox(dm - dw, dm + dw, -recess + 0.02f, -recess + 0.10f, gy, Mathf.Min(gy + 2.5f, yH), 1 | 4 | 8 | 16); }
            gfDarkUv = false;
            if (reach >= 0.9f)
            {
                GfMetal(gfKind == GfHotel ? new Vector3(0.45f, 0.42f, 0.40f) : new Vector3(0.50f, 0.52f, 0.55f));
                GfBox(s0 - 0.3f, s1 + 0.3f, 0.0f, reach, cy, cy + 0.30f, 1 | 4 | 8 | 16 | 32);
                GroundStats.canopies++;
            }
            Bucket.NoCollide = false;
            // the light under it (drawn by the night's lamp field: lit, no post)
            var lampAt = GP(dm, Mathf.Min(0.6f, Mathf.Max(0.2f, reach * 0.5f)), cy - 0.05f);
            gfTm.lamps.Add(new Lamp { foot = new Vector3(lampAt.x, gy, lampAt.z), head = lampAt, height = cy - 0.05f - gy, kind = LampDeck, breakaway = true });
            if (gfKind != GfHotel) return;
            // a hotel: two white planters flank the door, a blade sign on the pier beside it
            if (gfOff >= 3.4f)
            {
                GfPlanter(s0 + 0.15f, 0.95f, gy);
                GfPlanter(s1 - 0.15f, 0.95f, gy);
            }
            GfBlade(s0 - 0.4f, gfSplit + 0.5f, GfHotelNames[(int)((gfHash >> 11) % (uint)GfHotelNames.Length)],
                    ((gfHash >> 15) & 1u) == 0u ? new Vector3(0.70f, 0.90f, 1.30f) : new Vector3(1.20f, 0.55f, 0.50f), false);
        }

        /// <summary>A square white planter with a shrub, its middle at (s, o), on the walk (solid as drawn).</summary>
        static void GfPlanter(float s, float o, float gy)
        {
            var p = GW(s, o);
            if (!gfMap.FootprintClear(p, 0.5f)) return;
            gfDarkUv = false;
            bool nc = Bucket.NoCollide;
            Bucket.NoCollide = RaceRunOff.Inside(gfMap, gfTrims, p);   // drawn only in a race route's run-off, as its posts are
            float y = Mathf.Min(gy, CityElevation.GroundY(gfMap, p.x, p.y)) - 0.1f;
            GfStone(new Vector3(1.60f, 1.58f, 1.52f));
            GfBox(s - 0.45f, s + 0.45f, o - 0.45f, o + 0.45f, y, gy + 0.95f, 1 | 2 | 4 | 8 | 16);
            gfDarkUv = true; GfDark(DarkLeaf);
            GfBox(s - 0.36f, s + 0.36f, o - 0.36f, o + 0.36f, gy + 0.95f, gy + 1.55f, 1 | 2 | 4 | 8 | 16);
            gfDarkUv = false;
            Bucket.NoCollide = nc;
            GroundStats.planters++;
        }

        // ------------------------------------------------------------------
        //  THE BLADE SIGNS. A blade stands square off the wall, its name down
        //  it in block letters: PARK on a deck's, a made-up name on a hotel's
        //  (Docs: NO BRANDS - none of these is anyone's). A 5 x 7 block letter
        //  is its rows' runs of lit cells, a quad a run, on both faces.
        // ------------------------------------------------------------------
        static readonly string[] GfHotelNames = { "ARDEN", "CALDER", "WRENLY", "MERRIT", "HOLLIS", "TARLOW" };
        static readonly Dictionary<char, string> GfFont = new Dictionary<char, string>
        {
            { 'A', "01110,10001,10001,11111,10001,10001,10001" }, { 'C', "01110,10001,10000,10000,10000,10001,01110" },
            { 'D', "11110,10001,10001,10001,10001,10001,11110" }, { 'E', "11111,10000,10000,11110,10000,10000,11111" },
            { 'H', "10001,10001,10001,11111,10001,10001,10001" }, { 'I', "11111,00100,00100,00100,00100,00100,11111" },
            { 'K', "10001,10010,10100,11000,10100,10010,10001" }, { 'L', "10000,10000,10000,10000,10000,10000,11111" },
            { 'M', "10001,11011,10101,10101,10001,10001,10001" }, { 'N', "10001,11001,10101,10011,10001,10001,10001" },
            { 'O', "01110,10001,10001,10001,10001,10001,01110" }, { 'P', "11110,10001,10001,11110,10000,10000,10000" },
            { 'R', "11110,10001,10001,11110,10100,10010,10001" }, { 'S', "01111,10000,10000,01110,00001,00001,11110" },
            { 'T', "11111,00100,00100,00100,00100,00100,00100" }, { 'W', "10001,10001,10001,10101,10101,11011,10001" },
            { 'Y', "10001,10001,01010,00100,00100,00100,00100" },
        };
        const float GfPx = 0.075f, GfBladeOut = 0.72f, GfBladeT = 0.16f;

        /// <summary>A blade sign at <paramref name="s"/> along the wall, its foot at <paramref name="yb"/>: the name reads down it, on both faces.</summary>
        static void GfBlade(float s, float yb, string name, Vector3 colour, bool dark)
        {
            s = Mathf.Clamp(s, 0.3f, gfLen - 0.3f);
            float tall = name.Length * 8f * GfPx + 3f * GfPx;
            if (yb + tall > gfTop - 0.3f) return;
            bool nc = Bucket.NoCollide;
            Bucket.NoCollide = true;
            float t0 = s - GfBladeT * 0.5f, t1 = s + GfBladeT * 0.5f;
            if (dark) { gfDarkUv = true; GfDark(colour); } else { gfDarkUv = false; GfMetal(colour); }
            GfBox(t0, t1, 0.08f, 0.08f + GfBladeOut, yb, yb + tall, 1 | 4 | 8 | 16 | 32);
            // the letters: white, 1.5 cm proud of each face, the name reading down
            gfDarkUv = false; GfMetal(new Vector3(1.80f, 1.80f, 1.76f));
            float oL = 0.08f + (GfBladeOut - 5f * GfPx) * 0.5f;
            for (int li = 0; li < name.Length; li++)
            {
                if (!GfFont.TryGetValue(name[li], out string g)) continue;
                float yTop = yb + tall - 1.5f * GfPx - li * 8f * GfPx;
                for (int r = 0; r < 7; r++)
                {
                    int c = 0;
                    while (c < 5)
                    {
                        if (g[r * 6 + c] != '1') { c++; continue; }
                        int c1 = c; while (c1 < 5 && g[r * 6 + c1] == '1') c1++;
                        float ya = yTop - (r + 1) * GfPx, yc = yTop - r * GfPx;
                        // the face that looks back along the wall reads left to right from the wall out; the other mirrored
                        GfSide(t0 - 0.015f, oL + c * GfPx, oL + c1 * GfPx, ya, yc, -1);
                        GfSide(t1 + 0.015f, oL + (5 - c1) * GfPx, oL + (5 - c) * GfPx, ya, yc, 1);
                        c = c1;
                    }
                }
            }
            Bucket.NoCollide = nc;
            GroundStats.blades++;
        }

        // ------------------------------------------------------------------
        //  SHOPS
        // ------------------------------------------------------------------
        static void GfShops(float y0, uint h)
        {
            float len = gfLen;
            const float pw = 0.5f, recess = 0.4f;
            float endW = recess + pw * 0.5f;
            var stone = GfStoneOf();
            gfDarkUv = false;
            if (len < 2f * endW + 2.2f) { GfStone(stone * 0.92f); GfFront(0f, len, 0f, y0, gfSplit); return; }
            float bayM = 5.0f + 1.4f * (((gfHash >> 13) & 0xFF) / 255f);
            int nb = Mathf.Max(1, Mathf.RoundToInt((len - 2f * endW + pw) / bayM));
            float bw = (len - 2f * endW + pw) / nb;
            float head = Mathf.Min(0.80f, (gfSplit - gfFloor) * 0.2f), yH = gfSplit - head;
            var wallTint = Bucket.Tint;
            for (int b = 0; b <= nb; b++)
            {
                float c0 = endW - pw * 0.5f + b * bw;
                float s0 = b == 0 ? 0f : c0 - pw * 0.5f, s1 = b == nb ? len : c0 + pw * 0.5f;
                GfStone(stone);
                GfFront(s0, s1, 0f, y0, gfSplit);
                if (b > 0) GfSide(s0, 0f, -recess, y0, yH, -1);
                if (b < nb) GfSide(s1, 0f, -recess, y0, yH, 1);
                if (b == nb) break;
                float e1 = b + 1 == nb ? len - endW : c0 + bw - pw * 0.5f;
                float sm = (s1 + e1) * 0.5f;
                uint hb = h ^ ((uint)(b + 1) * 0x9E3779B1u); hb ^= hb >> 15; hb *= 0x85EBCA6Bu; hb ^= hb >> 13;
                // the sign band over the shop: a plain panel in the bay's colour
                GfMetal(SignCols[(hb >> 8) % (uint)SignCols.Length]);
                GfFront(s1, e1, 0f, yH, gfSplit);
                if (!GfInside(sm, -recess - 0.4f)) { GfStone(stone); GfFront(s1, e1, 0f, y0, yH); continue; }
                float gy = GfFloorAt(sm, recess);
                GfStone(stone * 0.85f);
                GfFront(s1, e1, 0f, y0, gy);
                GfFlat(s1, e1, 0f, -recess, gy, true);
                // the stall riser and the glass over it
                float riser = 0.45f;
                gfDarkUv = true; GfDark(((hb >> 3) & 1u) == 0u ? DarkGrey : DarkBronze);
                GfFront(s1, e1, -recess, gy, gy + riser);
                gfDarkUv = false;
                bool bright = ((hb >> 5) & 3u) != 0u;
                GfGlass(s1, e1, -recess, gy + riser, yH, bright ? LookSilver : LookBlue, bright ? new Vector3(0.80f, 0.84f, 0.88f) : new Vector3(1.30f, 1.40f, 1.55f), 1f, 1.9f);
                Bucket.NoCollide = true;
                GfStone(new Vector3(1.20f, 1.18f, 1.12f));
                GfFlat(s1, e1, 0f, -recess, yH, false);
                // the awning: a sloped sheet in the bay's colour, its valance and cheeks
                int aw = (int)((hb >> 12) % 10u);
                float reach = Mathf.Min(1.25f, gfOff - 1.6f);
                if (aw < 6 && reach > 0.6f && yH - gy > 2.5f)
                {
                    GfMetal(AwningCols[(hb >> 16) % (uint)AwningCols.Length]);
                    float ya = yH + 0.02f, yb = Mathf.Max(yH - 0.55f, gy + 2.25f), a0 = s1 - 0.1f, a1 = e1 + 0.1f;
                    Vector3 p0 = GP(a0, 0.03f, ya), p1 = GP(a1, 0.03f, ya), p2 = GP(a1, reach, yb), p3 = GP(a0, reach, yb);
                    var uv = new Vector2(0f, PartV(ya));
                    var up = Vector3.up + new Vector3(gfN.x, 0f, gfN.y) * 0.5f;
                    GfBk.Face(p0, p1, p2, p3, up, uv, new Vector2(a1 - a0, uv.y), new Vector2(a1 - a0, uv.y + 0.15f), new Vector2(0f, uv.y + 0.15f));
                    GfBk.Face(p0, p1, p2, p3, -up, uv, new Vector2(a1 - a0, uv.y), new Vector2(a1 - a0, uv.y + 0.15f), new Vector2(0f, uv.y + 0.15f));
                    GfFront(a0, a1, reach, yb - 0.22f, yb);
                    GfFront(a0, a1, reach, yb - 0.22f, yb, true);
                    var uq = new Vector2(0.1f, uv.y);
                    GfBk.Tri(p0, p3, GP(a0, 0.03f, yb), uq, uq, uq); GfBk.Tri(p0, GP(a0, 0.03f, yb), p3, uq, uq, uq);
                    GfBk.Tri(p1, p2, GP(a1, 0.03f, yb), uq, uq, uq); GfBk.Tri(p1, GP(a1, 0.03f, yb), p2, uq, uq, uq);
                }
                Bucket.NoCollide = false;
                // a main street's wide walk: a patio rail or a hedge planter in front of some bays
                if (gfDress == DressMain && gfOff >= 3.9f && e1 - s1 > 3f)
                {
                    int q = (int)((hb >> 20) % 100u);
                    if (q < 20) GfPatio(s1 + 0.2f, e1 - 0.2f, gy);
                    else if (q < 36) GfHedge(s1 + 0.4f, e1 - 0.4f, gy);
                }
            }
            Bucket.Tint = wallTint;
        }

        /// <summary>A cafe's patio rail on the walk in front of a bay: black, a metre high, three sides (solid as drawn, both faces).</summary>
        static void GfPatio(float s0, float s1, float gy)
        {
            const float outM = 1.9f, t = 0.06f, tall = 0.95f;
            for (int q = 0; q <= 2; q++) { var p = GW(Mathf.Lerp(s0, s1, q * 0.5f), outM); if (!gfMap.FootprintClear(p, 0.4f)) return; }
            float y = gy - 0.25f;
            bool nc = Bucket.NoCollide;
            Bucket.NoCollide = RaceRunOff.Inside(gfMap, gfTrims, GW((s0 + s1) * 0.5f, outM));
            gfDarkUv = true; GfDark(DarkBlack);
            GfBox(s0, s1, outM - t, outM, y, gy + tall, 1 | 2 | 16);
            GfBox(s0, s0 + t, 0.05f, outM - t, y, gy + tall, 4 | 8 | 16);
            GfBox(s1 - t, s1, 0.05f, outM - t, y, gy + tall, 4 | 8 | 16);
            gfDarkUv = false;
            Bucket.NoCollide = nc;
            GroundStats.patios++;
        }

        /// <summary>A hedge in a stone planter along a bay, on the walk (solid as drawn).</summary>
        static void GfHedge(float s0, float s1, float gy)
        {
            const float o0 = 1.0f, o1 = 1.75f;
            for (int q = 0; q <= 2; q++) { var p = GW(Mathf.Lerp(s0, s1, q * 0.5f), (o0 + o1) * 0.5f); if (!gfMap.FootprintClear(p, 0.5f)) return; }
            float y = gy - 0.25f;
            bool nc = Bucket.NoCollide;
            Bucket.NoCollide = RaceRunOff.Inside(gfMap, gfTrims, GW((s0 + s1) * 0.5f, o1));
            gfDarkUv = false;
            GfStone(new Vector3(0.95f, 0.93f, 0.88f));
            GfBox(s0, s1, o0, o1, y, gy + 0.50f, 1 | 2 | 4 | 8 | 16);
            gfDarkUv = true; GfDark(DarkLeaf);
            GfBox(s0 + 0.08f, s1 - 0.08f, o0 + 0.08f, o1 - 0.08f, gy + 0.50f, gy + 1.15f, 1 | 2 | 4 | 8 | 16);
            gfDarkUv = false;
            Bucket.NoCollide = nc;
            GroundStats.hedges++;
        }

        // ------------------------------------------------------------------
        //  BLANK: a solid base, a string course, a loading dock or two
        // ------------------------------------------------------------------
        static void GfBlank(float y0, uint h, bool front)
        {
            float len = gfLen;
            var stone = GfStoneOf() * 0.94f;
            gfDarkUv = false;
            bool wantDocks = gfPlace >= 0 ? GfPlaces[gfPlace].docks : gfKind != GfWorship && ((h >> 9) & 1u) == 0u;
            int docks = !wantDocks || len < 14f ? 0 : len < 34f ? 1 : 2;
            const float dockW = 3.8f, dockIn = 0.8f;
            float cursor = 0f;
            for (int k = 0; k < docks; k++)
            {
                float t = docks == 1 ? 0.3f + 0.4f * (((h >> 12) & 0xFF) / 255f) : (k == 0 ? 0.22f : 0.70f) + 0.08f * (((h >> (12 + 8 * k)) & 0xFF) / 255f);
                float dm = Mathf.Clamp(len * t, dockW * 0.5f + 1.2f, len - dockW * 0.5f - 1.2f);
                float d0 = dm - dockW * 0.5f, d1 = dm + dockW * 0.5f;
                if (d0 < cursor + 0.5f || !GfInside(dm, -dockIn - 0.4f)) continue;
                float gy = GfFloorAt(dm, dockIn);
                float dh = Mathf.Min(3.7f, gfSplit - gy - 0.45f);
                if (dh < 2.4f) continue;
                GfStone(stone);
                GfFront(cursor, d0, 0f, y0, gfSplit);
                GfFront(d0, d1, 0f, gy + dh, gfSplit);
                GfFront(d0, d1, 0f, y0, gy);
                GfSide(d0, 0f, -dockIn, gy, gy + dh, 1);
                GfSide(d1, 0f, -dockIn, gy, gy + dh, -1);
                GfStone(stone * 0.75f);
                GfFlat(d0, d1, 0f, -dockIn, gy, true);
                Bucket.NoCollide = true;
                GfFlat(d0, d1, 0f, -dockIn, gy + dh, false);
                Bucket.NoCollide = false;
                // the roll-up door: dark slats
                gfDarkUv = true; GfDark(DarkDoor);
                GfFront(d0, d1, -dockIn, gy, gy + dh);
                gfDarkUv = false;
                cursor = d1;
                GroundStats.docks++;
            }
            GfStone(stone);
            GfFront(cursor, len, 0f, y0, gfSplit);
            // the string course at the band's top, proud of the wall
            Bucket.NoCollide = true;
            GfStone(stone * 1.12f);
            GfBox(0f, len, 0f, 0.12f, gfSplit - 0.30f, gfSplit, 1 | 16 | 32);
            Bucket.NoCollide = false;
        }

        // ------------------------------------------------------------------
        //  THE PARKING DECK: every wall, every level
        // ------------------------------------------------------------------
        const float DeckLevelM = 3.05f, DeckParapetM = 1.05f, DeckSlabM = 0.28f, DeckVoidIn = 0.45f, DeckColW = 0.60f, DeckColPitchM = 8.5f, DeckDoorW = 7.2f;

        static void GfDeckWall(float y0, float y1)
        {
            float len = gfLen;
            uint hd = gfHash;
            int pal = (int)((hd >> 7) % 6u);
            Vector3 precast = pal == 0 ? new Vector3(1.30f, 1.27f, 1.20f) : pal == 1 ? new Vector3(1.15f, 1.05f, 0.90f) : pal == 2 ? new Vector3(0.95f, 0.95f, 0.97f)
                            : pal == 3 ? new Vector3(1.45f, 1.44f, 1.40f) : pal == 4 ? new Vector3(0.84f, 0.47f, 0.38f) : new Vector3(1.20f, 1.12f, 1.02f);
            float floor = gfFloor, top = y1;
            int levels = Mathf.Max(1, Mathf.RoundToInt((top - floor) / DeckLevelM));
            float lh = (top - floor) / levels;
            GroundStats.deckWalls++;
            gfDarkUv = false;
            if (len < 1.6f) { GfStone(precast); GfFront(0f, len, 0f, y0, top); return; }
            // the entrance, where the deck's door point lies on this wall
            bool door = false; float dm = 0f;
            if (gfHasDoor)
            {
                float sd = Vector2.Dot(gfDoorAt - gfA, gfD), od = Vector2.Dot(gfDoorAt - gfA, gfN);
                if (Mathf.Abs(od) < 2.5f && sd > -1f && sd < len + 1f && len >= DeckDoorW + 1.6f)
                {
                    dm = Mathf.Clamp(sd, DeckDoorW * 0.5f + 0.7f, len - DeckDoorW * 0.5f - 0.7f);
                    // a mouth needs a street in front of it
                    door = GfStreet(gfA, gfA + gfD * len, gfN, out int ei, out float arc, out gfOff, out gfDress);
                    if (door) { gfRoadYm = gfMap.edges[ei].YAt(arc); gfRoadY0 = gfRoadY1 = gfRoadYm; }
                }
            }
            float d0 = dm - DeckDoorW * 0.5f, d1 = dm + DeckDoorW * 0.5f;
            // the dark of the floors behind, set back; the ground storey's low wall; the columns; the spandrels
            bool deepOk = GfInside(len * 0.5f, -DeckVoidIn - 0.3f);
            float voidIn = deepOk ? DeckVoidIn : 0.05f;
            gfDarkUv = true; GfDark(DarkGrey);
            if (door) { GfFront(0f, d0, -voidIn, y0, top); GfFront(d1, len, -voidIn, y0, top); GfFront(d0, d1, -voidIn, floor + lh - DeckSlabM, top); }
            else GfFront(0f, len, -voidIn, y0, top);
            gfDarkUv = false;
            GfStone(precast * 0.90f);
            int nc = Mathf.Max(1, Mathf.RoundToInt(len / DeckColPitchM));
            float cw = len / nc;
            for (int k = 0; k <= nc; k++)
            {
                float cs = k * cw;
                float s0 = Mathf.Max(0f, cs - DeckColW * 0.5f), s1 = Mathf.Min(len, cs + DeckColW * 0.5f);
                if (door && s1 > d0 - 0.3f && s0 < d1 + 0.3f)
                {
                    // a column in the mouth moves to its jamb
                    if (cs < dm) { s1 = d0; s0 = d0 - DeckColW; } else { s0 = d1; s1 = d1 + DeckColW; }
                    if (s0 < 0f || s1 > len) continue;
                }
                GfFront(s0, s1, 0f, y0, top);
                if (k > 0) GfSide(s0, 0f, -voidIn, y0, top, -1);
                if (k < nc) GfSide(s1, 0f, -voidIn, y0, top, 1);
            }
            GfStone(precast);
            const float proud = 0.12f;
            for (int k = 0; k <= levels; k++)
            {
                float ya = k == 0 ? y0 : floor + k * lh - DeckSlabM;
                float yb = k == levels ? top : floor + k * lh + DeckParapetM;
                if (k == 0) yb = GfDeckSill(len) + DeckParapetM;
                if (yb > top) yb = top;
                if (door && k == 0)
                {
                    GfBox(0f, d0, 0f, proud, ya, yb, 1 | 8 | 16);
                    GfBox(d1, len, 0f, proud, ya, yb, 1 | 4 | 16);
                    GfFlat(0f, d0, 0f, -voidIn, yb, true); GfFlat(d1, len, 0f, -voidIn, yb, true);
                }
                else
                {
                    GfBox(0f, len, 0f, proud, ya, yb, 1 | 16 | (k > 0 ? 32 : 0));
                    if (k < levels) GfFlat(0f, len, 0f, -voidIn, yb, true);
                    if (k > 0) { Bucket.NoCollide = true; GfFlat(0f, len, 0f, -voidIn, ya, false); Bucket.NoCollide = false; }
                }
            }
            if (door) GfDeckDoor(d0, d1, dm, floor + lh - DeckSlabM);
        }

        /// <summary>The ground under a deck wall's middle (its low wall stands a parapet over it).</summary>
        static float GfDeckSill(float len)
        {
            var p = GW(len * 0.5f, 1.0f);
            return Mathf.Max(gfFloor, CityElevation.GroundY(gfMap, p.x, p.y));
        }

        /// <summary>A deck's mouth: two lanes into the dark, five red bollards, a clearance bar, a light under the soffit, the blade sign.</summary>
        static void GfDeckDoor(float d0, float d1, float dm, float yHead)
        {
            float gy = GfFloorAt(dm, 1.0f) - 0.02f;
            float deep = 6.0f;
            while (deep > 1.0f && !(GfInside(dm, -deep - 0.3f) && GfInside(d0 + 0.3f, -deep - 0.3f) && GfInside(d1 - 0.3f, -deep - 0.3f))) deep -= 1.0f;
            if (deep < 1.0f) deep = DeckVoidIn;
            // the mouth: floor, walls, back and ceiling, dark
            gfDarkUv = true; GfDark(DarkGrey);
            GfFlat(d0, d1, 0f, -deep, gy, true);
            GfSide(d0, 0f, -deep, gfY0, yHead, 1);
            GfSide(d1, 0f, -deep, gfY0, yHead, -1);
            GfFront(d0, d1, -deep, gfY0, yHead);
            Bucket.NoCollide = true;
            GfFlat(d0, d1, -DeckVoidIn, -deep, yHead, false);   // (the spandrel's own soffit covers the first of it)
            Bucket.NoCollide = false;
            // the step at the wall line (the walk's back edge to the deck's floor)
            GfFront(d0, d1, 0f, gfY0, gy);
            // five red bollards: two a jamb, one between the lanes
            GfDark(DarkRed);
            float[] at = { d0 + 0.25f, d0 + 0.85f, dm, d1 - 0.85f, d1 - 0.25f };
            Bucket.NoCollide = RaceRunOff.Inside(gfMap, gfTrims, GW(dm, 0.4f));   // drawn only in a race route's run-off
            foreach (float s in at) GfBox(s - 0.11f, s + 0.11f, 0.25f, 0.47f, gy - 0.3f, gy + 1.05f, 1 | 2 | 4 | 8 | 16);
            Bucket.NoCollide = false;
            // the clearance bar: yellow, hung under the head
            Bucket.NoCollide = true;
            gfDarkUv = false; GfMetal(new Vector3(1.90f, 1.55f, 0.40f));
            float by = Mathf.Min(gy + 2.35f, yHead - 0.25f);
            GfBox(d0 + 0.5f, d1 - 0.5f, 0.10f, 0.22f, by, by + 0.14f, 1 | 2 | 16 | 32);
            Bucket.NoCollide = false;
            var lampAt = GP(dm, -0.8f, yHead - 0.08f);
            gfTm.lamps.Add(new Lamp { foot = new Vector3(lampAt.x, gy, lampAt.z), head = lampAt, height = yHead - 0.08f - gy, kind = LampDeck, breakaway = true });
            // the blade sign beside the mouth: red, PARK down it
            gfDarkUv = false;
            GfBlade(d1 + DeckColW * 0.5f + 0.2f, gy + 3.4f, "PARK", DarkRed, true);
            GroundStats.deckDoors++;
        }
    }
}
