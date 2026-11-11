using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  THE MEDIAN DRESS (2026-11-09). The owner, with road-level photos of
    //  NC-16 Brookshire Blvd by N Crigler St: the real median is a raised
    //  curbed island PLANTED with mulch, low shrubs and ornamental grass,
    //  plain concrete only at its tapered nose and where it necks down beside
    //  a left-turn pocket; the game drew a bare wall on a strip (a trunk) or
    //  a grass-topped island end to end (every other divided street).
    //
    //  THE EVIDENCE (37 places on 13 divided arterials at road level, the
    //  hand-back's table): a curbed median is CONCRETE where it is narrow
    //  (1-2 m: Wilkinson, N Tryon, South Blvd, Providence beside its turn
    //  lanes), at every nose, and along the turn pocket before an opening;
    //  between openings a median of 2.5-6 m is a MULCH BED (Brookshire by N
    //  Crigler: ornamental grass in masses and low shrubs; W.T. Harris:
    //  mulch and trees), a wider one mown GRASS (Johnston Rd, with a tree
    //  row); a 55 mph trunk's is grass with no bed (Billy Graham Pkwy,
    //  Brookshire north of I-85, University City Blvd). No wall in any.
    //
    //  THE RULE, on what the island already knows (CityMeshes.CurbMedian's
    //  sections): the island's top is, per span,
    //    CONCRETE  where the median is under MedianConcreteMaxW from edge to
    //              edge, within MedianPocketM along the road of where the
    //              island stops (a nose, an opening: the turn pocket's
    //              length), where the bed between two stops would be under
    //              MedianBedMinM, and on the roads the survey found concrete
    //              from end to end (MedianConcreteRoads, MedianConcreteWays);
    //    A BED     (the MedianBed slot: bark mulch) up to MedianBedMaxW wide,
    //              planted in 12 m blocks: ornamental grass in a mass, low
    //              shrubs, or bare mulch - cards of the roadside understory's
    //              atlas (CityUnderstory.Card), one render-only mesh a tile
    //              (TileMeshes.medianPlants: no collider, a car drives through
    //              a shrub; the curb is the obstacle there always was);
    //    GRASS     wider than that, on a fast trunk (80 km/h and over, or an
    //              untagged one with no shopfront near), and wherever the
    //              island is the wide median's grass at grade (no curb).
    //  Both halves of an island decide alike: the twin's node openings are
    //  read from this side. The island's shape, heights, collider and where
    //  it stands are CurbMedian's, unchanged.
    //  PSX_CITY_MEDIANDRESS=0: the grass top as before; PSX_CITY_MEDIANPLANTS=0:
    //  the surfaces without the plants (the frame-cost A/B).
    // ======================================================================
    public static partial class CityMeshes
    {
        public static readonly bool MedianDressOn = System.Environment.GetEnvironmentVariable("PSX_CITY_MEDIANDRESS") != "0";
        public static readonly bool MedianPlantsOn = MedianDressOn && System.Environment.GetEnvironmentVariable("PSX_CITY_MEDIANPLANTS") != "0";

        /// <summary>Under this from edge to edge the island is concrete (the
        /// monolithic islands of Wilkinson, N Tryon, South Blvd: 1-2 m).</summary>
        public const float MedianConcreteMaxW = 2.4f;
        /// <summary>A bed up to this wide; wider is mown grass.</summary>
        public const float MedianBedMaxW = 6f;
        /// <summary>Concrete this far along the road from where the island
        /// stops: the nose and the left-turn pocket beside it (Brookshire by N
        /// Crigler: about 25 m of concrete before the nose).</summary>
        public const float MedianPocketM = 25f;
        /// <summary>No bed shorter than this between two pockets.</summary>
        public const float MedianBedMinM = 20f;
        /// <summary>How far past the tile the island is read (CurbMedian's
        /// near): a stop this near is known to every tile that draws a
        /// section beside it, so two tiles never dress one island two ways.</summary>
        const float MedianEvalNearM = 45f;
        /// <summary>The plants' block along the road, their step in it, how
        /// far in from the curb's nose they stand and from a bed's end.</summary>
        const float MedianBlockM = 12f, MedianPlantStepM = 0.9f, MedianPlantInM = 0.55f, MedianPlantEndM = 2f;

        /// <summary>Roads whose median the survey found concrete at every
        /// place looked at (2026-11-09: Wilkinson Blvd 3 of 3, N Tryon St 3 of
        /// 3, South Blvd 3 of 4): commercial strips with an opening a block.</summary>
        static readonly HashSet<string> MedianConcreteRoads = new HashSet<string>
        {
            "Wilkinson Boulevard", "North Tryon Street", "South Boulevard",
        };

        public const int MedianTopGrass = 0, MedianTopConcrete = 1, MedianTopBed = 2;

        // ---- the arterials pass (2026-11-10): what the median pass left ----------
        /// <summary>TREE ROWS. The survey's two tree-lined medians (E W.T. Harris
        /// Blvd: mulch and trees; Johnston Rd: mown grass and a tree row): one
        /// tree on the island's axis every MedianTreePitchM, where the island
        /// is MedianTreeMinW wide or more and MedianTreeEndM clear of the
        /// concrete at a nose or pocket (the sight line at an opening). Planted
        /// as the city's street trees are (CityTrees.PlantStreet: a clear stem,
        /// the trunk solid by the city's rule, breakaway inside a race route's
        /// run-off). PSX_CITY_MEDIANTREES=0: none.</summary>
        public static readonly bool MedianTreesOn = MedianDressOn && System.Environment.GetEnvironmentVariable("PSX_CITY_MEDIANTREES") != "0";
        public const int DressMedian = 3;
        public const float MedianTreePitchM = 13.5f, MedianTreeMinW = 3f, MedianTreeEndM = 5f;
        static readonly HashSet<string> MedianTreeRoads = new HashSet<string>
        {
            "East W T Harris Boulevard", "West W T Harris Boulevard", "Johnston Road",
        };
        public static int MedianTreeCount;
        /// <summary>NO CURB ON A FAST TRUNK'S GRASS. The survey's 55 mph trunks
        /// (Billy Graham Pkwy, Brookshire Blvd north of I-85, University City
        /// Blvd): grass between the carriageways at their own grade, no curb.
        /// A fast pair's median of MedianConcreteMaxW and over is laid as the
        /// wide median's flat grass. PSX_CITY_FASTMEDIAN_CURB=1: curbed as before.</summary>
        public static readonly bool FastMedianFlatOn = MedianDressOn && System.Environment.GetEnvironmentVariable("PSX_CITY_FASTMEDIAN_CURB") != "1";
        /// <summary>THE REFUGE ISLANDS (the 17 kerbed pairs of LineModel.RefugeList)
        /// take the dress too. PSX_CITY_REFUGEDRESS=0: grass as before.</summary>
        public static readonly bool RefugeDressOn = MedianDressOn && System.Environment.GetEnvironmentVariable("PSX_CITY_REFUGEDRESS") != "0";
        public static int RefugeDressSpans;
        /// <summary>THE SCATTERED SHRUBS on a grass island (wider than a bed):
        /// the understory's own, as before the median dress. PSX_CITY_MEDIANSHRUBS=0:
        /// none on any island.</summary>
        public static readonly bool MedianShrubsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_MEDIANSHRUBS") != "0";
        /// <summary>For the understory: may a shrub stand on this carriageway's
        /// grass island at p - not on a road whose island is concrete, not
        /// within the concrete of a nose or pocket (from the map alone).</summary>
        public static bool MedianShrubOk(CityMap map, CityMap.Edge e, Vector2 p)
        {
            if (MedianConcreteRoads.Contains(e.name) || MedianConcreteWays.Contains(e.wayId)) return false;
            CityElevation.ProjectOn(e, p, out float s);
            float ba = MedianBeyond(map, e, e.a), bb = MedianBeyond(map, e, e.b);
            float d = Mathf.Min(ba == float.MaxValue ? float.MaxValue : s + ba, bb == float.MaxValue ? float.MaxValue : e.length - s + bb);
            return d >= MedianPocketM + 8f;
        }
        /// <summary>For the understory: an island up to this wide is dressed by its own rule.</summary>
        public static float MedianOwnDressMaxW => MedianShrubsOn ? MedianBedMaxW : CurbMedianMaxW;

        /// <summary>For the understory (CityUnderstory): the curbed island's widest.</summary>
        public const float CurbMedianMaxW = CurbMedianMaxM;
        /// <summary>For the understory: is this carriageway's left a surface
        /// trunk's island median (its twin within reach)?</summary>
        public static bool IslandMedianSide(CityMap map, CityMap.Edge e) =>
            MedianDressOn && DividerAt(e) == DividerKind.Island && CurbMedianClass(e) && CurbMedianTwin(map, e);
        /// <summary>For the understory: two carriageways of one divided
        /// street, both of the island's class.</summary>
        public static bool MedianPair(CityMap.Edge a, CityMap.Edge b) =>
            a.index != b.index && a.oneway && b.oneway && a.name == b.name && CurbMedianOn && CurbMedianClass(a) && CurbMedianClass(b);

        /// <summary>Per section of the edge being drawn (PrepareCurbMedian):
        /// the median's width edge to edge, how far back and forward along
        /// the road the island stops, whether the section was read, the
        /// pair's fast flag, and the top's kind.</summary>
        static float[] cmG = new float[128], cmDb = new float[128], cmDf = new float[128];
        static bool[] cmEv = new bool[128], cmFast = new bool[128];
        static byte[] cmTop = new byte[128];
        static int[] cmTw = new int[128];
        static float[] cmTwS = new float[128];

        public static int MedianBedSpans, MedianConcreteSpans, MedianGrassSpans, MedianPlantCount;
        public static float MedianBedM, MedianConcreteM, MedianGrassM;
        public static void MedianDressReset() { MedianBedSpans = MedianConcreteSpans = MedianGrassSpans = MedianPlantCount = MedianTreeCount = 0; MedianBedM = MedianConcreteM = MedianGrassM = 0f; }
        public static string MedianDressLine() =>
            $"median dress {(MedianDressOn ? "on" : "OFF")} (plants {(MedianPlantsOn ? "on" : "off")}, trunk islands {(TrunkIslandOn ? "on" : "OFF")}, trees {(MedianTreesOn ? "on" : "off")}, fast grass flat {(FastMedianFlatOn ? "on" : "off")}): island halves concrete {MedianConcreteM:0} m in {MedianConcreteSpans} spans, bed {MedianBedM:0} m in {MedianBedSpans}, grass {MedianGrassM:0} m in {MedianGrassSpans}; {MedianPlantCount} plants, {MedianTreeCount} trees";

        static void MedianDressSize(int n)
        {
            if (cmG.Length >= n) return;
            cmG = new float[n * 2]; cmDb = new float[n * 2]; cmDf = new float[n * 2];
            cmEv = new bool[n * 2]; cmFast = new bool[n * 2]; cmTop = new byte[n * 2];
            cmTw = new int[n * 2]; cmTwS = new float[n * 2];
        }

        // ---- the road's own facts ------------------------------------------

        static sbyte[] medUrbanCache;
        static CityMap medUrbanMap;
        /// <summary>A shopfront (any non-house building) within the walks'
        /// reach of the carriageway, or the urban core: the city's own
        /// landscaped medians are there.</summary>
        static bool MedianUrban(CityMap map, CityMap.Edge e)
        {
            lock (CurbMedianByEdge)
            {
                if (medUrbanMap != map || medUrbanCache == null || medUrbanCache.Length != map.edges.Length) { medUrbanCache = new sbyte[map.edges.Length]; medUrbanMap = map; }
                ref sbyte c = ref medUrbanCache[e.index];
                if (c == 0)
                {
                    bool u = false;
                    for (int k = 1; k <= 3 && !u; k++)
                    {
                        var p = e.PointAt(e.length * k * 0.25f);
                        u = Vector2.Distance(p, map.uptown) < UrbanCoreM || map.AnyFootprintNear(p, UrbanFootM, nonHouseOnly: true);
                    }
                    c = (sbyte)(u ? 1 : -1);
                }
                return c > 0;
            }
        }

        /// <summary>A fast trunk carriageway: 80 km/h and over, or untagged
        /// with no shopfront near. Its median is grass, never a bed.</summary>
        static bool MedianFast(CityMap map, CityMap.Edge e) =>
            e.cls >= 4 && (e.speedKmh >= 80 || (e.speedKmh <= 0 && !MedianUrban(map, e)));

        /// <summary>The carriageway going on from this node as an edge of the
        /// same road the same way (CurbMedianStops' own test), null: none.</summary>
        static CityMap.Edge MedianOnward(CityMap map, CityMap.Edge e, int node)
        {
            var ne = map.nodeEdges[node];
            if (ne == null) return null;
            foreach (int i in ne)
            {
                if (i == e.index) continue;
                var o = map.edges[i];
                if (o.name != e.name || !o.oneway) continue;
                bool onward = node == e.b ? o.a == node : o.b == node;
                if (!onward) continue;
                var te = node == e.b ? e.TangentAt(e.length) : e.TangentAt(0f);
                var to = node == e.b ? o.TangentAt(0f) : o.TangentAt(o.length);
                if (Vector2.Dot(te.normalized, to.normalized) < 0.5f) continue;
                return o;
            }
            return null;
        }

        /// <summary>Does a road leave this node on the carriageway's median
        /// side (its left): a cross street through the median, a crossover
        /// link - an opening?</summary>
        static bool MedianNodeOpens(CityMap map, CityMap.Edge e, int node)
        {
            var ne = map.nodeEdges[node];
            if (ne == null) return false;
            var te = (node == e.b ? e.TangentAt(e.length) : e.TangentAt(0f)).normalized;
            var left = new Vector2(-te.y, te.x);
            var on = MedianOnward(map, e, node);
            foreach (int i in ne)
            {
                if (i == e.index || (on != null && i == on.index)) continue;
                var o = map.edges[i];
                if (o.length < 0.5f) continue;
                // its direction away from the node
                var d = o.a == node ? o.TangentAt(Mathf.Min(1f, o.length)) : -o.TangentAt(Mathf.Max(0f, o.length - 1f));
                if (o.a == node && o.b == node) continue;
                if (Vector2.Dot(d.normalized, left) > 0.3f) return true;
            }
            return false;
        }

        static readonly Dictionary<int, float> medBeyond = new Dictionary<int, float>();
        static CityMap medBeyondMap;
        /// <summary>How far past this end node the island runs on before it
        /// stops or meets an opening (0: at the node itself; MaxValue: further
        /// than the dress reads). From the map alone.</summary>
        static float MedianBeyond(CityMap map, CityMap.Edge e, int node)
        {
            int key = e.index * 2 + (node == e.b ? 1 : 0);
            lock (medBeyond)
            {
                if (medBeyondMap != map) { medBeyond.Clear(); medBeyondMap = map; }
                if (medBeyond.TryGetValue(key, out float got)) return got;
            }
            float dist = 0f, res = float.MaxValue;
            var cur = e; int n = node;
            for (int guard = 0; guard < 12; guard++)
            {
                if (CurbMedianStops(map, cur, n) || MedianNodeOpens(map, cur, n)) { res = dist; break; }
                var o = MedianOnward(map, cur, n);
                if (o == null) { res = dist; break; }
                dist += o.length;
                if (dist > MedianEvalNearM + MedianPocketM) break;
                n = o.a == n ? o.b : o.a; cur = o;
            }
            lock (medBeyond) medBeyond[key] = res;
            return res;
        }

        // ---- the top's kind, per section ------------------------------------

        /// <summary>After PrepareCurbMedian's own loops: how far each island
        /// section is from where the island stops, back and forward, and its
        /// top's kind.</summary>
        static void PrepareMedianDress(CityMap map, CityMap.Edge e, int n)
        {
            // a listed way is concrete whatever its width (as before); a listed road where it is curbed
            bool concWay = MedianConcreteWays.Contains(e.wayId), concRoad = MedianDressOn && MedianConcreteRoads.Contains(e.name);
            float s0 = sections[0].s, s1 = sections[n - 1].s;
            float beyA = MedianDressOn ? MedianBeyond(map, e, e.a) : 0f, beyB = MedianDressOn ? MedianBeyond(map, e, e.b) : 0f;
            for (int k = 0; k < n; k++)
            {
                cmTop[k] = (byte)(concWay ? MedianTopConcrete : MedianTopGrass);
                if (cmW[k] <= 0f || !MedianDressOn) continue;
                float sk = sections[k].s;
                float db = beyA == float.MaxValue ? float.MaxValue : sk - s0 + beyA;
                float df = beyB == float.MaxValue ? float.MaxValue : s1 - sk + beyB;
                for (int j = 0; j < n; j++)
                {
                    if (!cmEv[j] || cmW[j] > 0f) continue;
                    float dj = sections[j].s - sk;
                    if (dj < 0f) db = Mathf.Min(db, -dj); else df = Mathf.Min(df, dj);
                }
                // the twin runs the other way: its end b is behind us, its end a ahead
                if (cmTw[k] >= 0)
                {
                    var tw = map.edges[cmTw[k]];
                    float tb = MedianBeyond(map, tw, tw.b), ta = MedianBeyond(map, tw, tw.a);
                    if (tb != float.MaxValue) db = Mathf.Min(db, tw.length - cmTwS[k] + tb);
                    if (ta != float.MaxValue) df = Mathf.Min(df, cmTwS[k] + ta);
                }
                cmDb[k] = db; cmDf[k] = df;
                if (concWay) continue;
                if (cmK[k] < 0.5f) continue;                                    // grass at grade: no curb, no bed
                if (concRoad) { cmTop[k] = MedianTopConcrete; continue; }
                float w = cmG[k];
                if (w < MedianConcreteMaxW) { cmTop[k] = MedianTopConcrete; continue; }
                if (cmFast[k]) continue;                                        // a fast trunk: grass
                float cap = MedianEvalNearM;
                if (db < MedianPocketM || df < MedianPocketM || Mathf.Min(db, cap) + Mathf.Min(df, cap) < 2f * MedianPocketM + MedianBedMinM)
                { cmTop[k] = MedianTopConcrete; continue; }
                cmTop[k] = (byte)(w < MedianBedMaxW ? MedianTopBed : MedianTopGrass);
            }
        }

        /// <summary>A span's top (sections i - 1 and i): concrete if either
        /// end is, else a bed if either is, else grass.</summary>
        static int MedianSpanTop(int i)
        {
            bool a = cmW[i - 1] > 0f, b = cmW[i] > 0f;
            int ka = a ? cmTop[i - 1] : cmTop[i], kb = b ? cmTop[i] : cmTop[i - 1];
            if (ka == MedianTopConcrete || kb == MedianTopConcrete) return MedianTopConcrete;
            if (!MedianDressOn) return ka;
            // an end without an island is where it stops: its nose
            if (!a || !b) return MedianTopConcrete;
            return ka == MedianTopBed || kb == MedianTopBed ? MedianTopBed : MedianTopGrass;
        }

        static Bucket MedianTopBucket(int kind) =>
            buckets[(int)(kind == MedianTopConcrete ? Slot.Pavement : kind == MedianTopBed ? Slot.MedianBed : Slot.Ground)];
        /// <summary>World-planar like the ground's (GroundUV): the bed's bark 4 m a repeat.</summary>
        static Vector2 MedianTopUV(int kind, float wx, float wz) =>
            kind == MedianTopBed ? new Vector2(wx / 4f, wz / 4f) : GroundUV(kind == MedianTopConcrete, wx, wz);

        // ---- the tree row ----------------------------------------------------

        /// <summary>A span's trees on the island's axis (the lower-numbered
        /// carriageway of the pair plants them), on a fixed grid of its arc.</summary>
        static void MedianTreeSpan(TileMeshes tm, CityMap.Edge e, int i, float sA, float sB, Vector3[] pA, Vector3[] pB)
        {
            if (!MedianTreesOn || !StreetDressOn || sB - sA < 0.05f || !MedianTreeRoads.Contains(e.name)) return;
            int tw = cmTw[i - 1] >= 0 ? cmTw[i - 1] : cmTw[i];
            if (tw < 0 || tw < e.index) return;
            var o = tm.origin;
            for (int k = Mathf.CeilToInt(sA / MedianTreePitchM - 0.5f); (k + 0.5f) * MedianTreePitchM < sB; k++)
            {
                float s = (k + 0.5f) * MedianTreePitchM;
                if (s < sA) continue;
                float f = (s - sA) / (sB - sA);
                if (Mathf.Lerp(cmG[i - 1], cmG[i], f) < MedianTreeMinW) continue;
                float db = Mathf.Lerp(Mathf.Min(cmDb[i - 1], 1000f), Mathf.Min(cmDb[i], 1000f), f), df = Mathf.Lerp(Mathf.Min(cmDf[i - 1], 1000f), Mathf.Min(cmDf[i], 1000f), f);
                if (Mathf.Min(db, df) < MedianPocketM + MedianTreeEndM) continue;
                var foot = Vector3.Lerp(pA[3], pB[3], f);
                float wx = foot.x + o.x, wz = foot.z + o.z;
                if (wx < o.x || wx >= o.x + TileSize || wz < o.z || wz >= o.z + TileSize) continue;
                tm.streetTrees.Add(new StreetTree { foot = foot, edge = e.index, side = 1, dress = (byte)DressMedian, station = (short)k });
                MedianTreeCount++;
            }
        }

        // ---- the plants ------------------------------------------------------

        static readonly List<Vector3> mpV = new List<Vector3>(2048), mpN = new List<Vector3>(2048);
        static readonly List<Vector2> mpUV = new List<Vector2>(2048);
        static readonly List<int> mpT = new List<int>(3072);

        static void MedianPlantsClear() { mpV.Clear(); mpN.Clear(); mpUV.Clear(); mpT.Clear(); }

        static Mesh MedianPlantsMesh()
        {
            if (mpV.Count == 0) return null;
            var m = new Mesh { name = "medianplants" };
            if (mpV.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(mpV);
            m.SetNormals(mpN);
            m.SetUVs(0, mpUV);
            m.SetTriangles(mpT, 0, false);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// A bed span's plants (its own half of the island: the twin plants
        /// the other), on a fixed grid of the edge's arc so two tiles and two
        /// spans never plant one place twice: by 12 m block a mass of
        /// ornamental grass (switchgrass and fescue, a metre and more high,
        /// a step apart, two deep where the half is wide), a group of low
        /// shrubs (inkberry and azalea kept about a metre, every other step)
        /// or bare mulch. Feet on the island's top.
        /// </summary>
        static void MedianPlantSpan(CityMap.Edge e, int i, Vector3 eA, Vector3 eB, Vector2 outA, Vector2 outB, float sA, float sB, Vector3[] pA, Vector3[] pB, float wA, float wB)
        {
            if (!MedianPlantsOn || sB - sA < 0.05f) return;
            float density = CityUnderstory.Density;
            for (float s = Mathf.Ceil(sA / MedianPlantStepM) * MedianPlantStepM; s < sB; s += MedianPlantStepM)
            {
                int st = Mathf.RoundToInt(s / MedianPlantStepM), block = Mathf.FloorToInt(s / MedianBlockM);
                float f = (s - sA) / (sB - sA);
                // clear of the bed's ends (the concrete begins there)
                float db = Mathf.Lerp(cmDb[i - 1], cmDb[i], f), df = Mathf.Lerp(cmDf[i - 1], cmDf[i], f);
                if (Mathf.Min(db, df) < MedianPocketM + MedianPlantEndM) continue;
                float bt = CityTrees.Hash01(e.index, block, 700);
                int kind = bt < 0.55f ? 0 : bt < 0.80f ? 1 : 2;       // grass mass, shrubs, bare
                if (kind == 2) continue;
                if (kind == 1 && (st & 1) != 0) continue;
                float w = Mathf.Lerp(wA, wB, f);
                float inMin = CurbNoseOut + MedianPlantInM;
                if (w < inMin + 0.1f) continue;
                var edge = Vector3.Lerp(eA, eB, f);
                var o = Vector2.Lerp(outA, outB, f).normalized;
                float yNose = Mathf.Lerp(pA[2].y, pB[2].y, f), yAxis = Mathf.Lerp(pA[3].y, pB[3].y, f);
                // a mass stands two deep where its half is wide enough: a band each
                int rows = kind == 0 && w - inMin > 0.9f ? 2 : 1;
                for (int row = 0; row < rows; row++)
                {
                    int salt = 710 + row * 10;
                    if (CityTrees.Hash01(e.index, st, salt) >= (kind == 0 ? 0.92f : 0.8f) * density) continue;
                    // across its half: from behind the curb to the axis
                    float lat = Mathf.Lerp(inMin, w, (row + CityTrees.Hash01(e.index, st, salt + 1)) / rows);
                    float y = Mathf.Lerp(yNose, yAxis, Mathf.Clamp01((lat - CurbNoseOut) / Mathf.Max(0.05f, w - CurbNoseOut)));
                    float along = (CityTrees.Hash01(e.index, st, salt + 2) - 0.5f) * 0.5f;
                    var foot = new Vector3(edge.x + o.x * lat - o.y * along, y, edge.z + o.y * lat + o.x * along);
                    float h2 = CityTrees.Hash01(e.index, st, salt + 3), h3 = CityTrees.Hash01(e.index, st, salt + 4);
                    int cell; float scale;
                    if (kind == 0) { cell = h2 < 0.7f ? CityUnderstory.CellSwitchgrass : CityUnderstory.CellFescue; scale = 0.95f + 0.4f * h3; }
                    else { cell = h2 < 0.6f ? CityUnderstory.CellInkberry : CityUnderstory.CellAzalea; scale = 0.5f + 0.2f * h3; }
                    CityUnderstory.Card(mpV, mpN, mpUV, mpT, foot, cell, scale, CityTrees.Hash01(e.index, st, salt + 5) * 180f);
                    MedianPlantCount++;
                }
            }
        }
    }
}
