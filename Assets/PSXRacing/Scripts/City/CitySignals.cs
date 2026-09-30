using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// CHARLOTTE'S JUNCTION FURNITURE, THE VISIBLE PART (plan WP-26 to WP-28):
    /// STOP signs and their stop bars at OSM's highway=stop, and traffic
    /// signals at highway=traffic_signals - mast arms uptown, span wires
    /// elsewhere (mast arms where a span will not string) - their heads facing
    /// every approach and cycling green, yellow, red in opposing pairs off ONE
    /// shared clock, glowing after dark like the street lamps. What traffic
    /// does at them (stopping, obeying the lights) is a later package: this
    /// only stands them up. <see cref="AspectOf"/> is the clock it will read.
    ///
    /// WHERE THE CONTROLS ARE. WP-10 resolved every tagged OSM node onto the
    /// raw ways and the export carries them (charlotte_city.bytes, section
    /// TAGN; <see cref="CityMap.tagged"/>): node, way, kind and (edge, s). A
    /// STOP's meaning at a junction node - stop=all, stop=minor,
    /// direction=forward|backward - comes from charlotte_stops.bytes
    /// (tools/city/stop_tags.mjs), keyed by node id.
    ///   * SIGNALS: a signal tagged at a junction node, or up to
    ///     <see cref="SignalSnapM"/> before one on its approach (a divided
    ///     road's are mapped on each carriageway), marks that node. Marked
    ///     nodes joined by an edge shorter than <see cref="ClusterM"/> are one
    ///     junction (a divided road's crossing is two to four graph nodes), and
    ///     a three-armed node just across one from a marked node joins it. Its
    ///     approaches are the edges that come INTO it from outside (a one-way
    ///     leaving it is none, a freeway mainline never). Opposing approaches
    ///     share a phase: the junction's main road (its biggest approach) and
    ///     whatever runs within 45 degrees of it are one pair, the rest the
    ///     other; half the junctions (a hash) start on the other pair.
    ///   * STOPS: at a junction node, stop=all stops every approach; a
    ///     direction stops that way's approach; otherwise the tagged way stops
    ///     if it is the node's minor road (two ways of one class: both stop,
    ///     which is the all-way stop the neighbourhoods are). A stop tagged on
    ///     an approach within <see cref="StopSnapM"/> of its junction stops that
    ///     approach. A signal wins over a stop at the same node.
    ///
    /// WHERE THEY STAND. Never in a lane, a driveway or a race run-off: a foot
    /// must be past the drawn pavement of EVERY road near it
    /// (<see cref="RoadsideOccupancy.RoadEdgeDistance"/>, the line model's
    /// edges - CityMap.Edge.PaveEdgeM's), on no building, lot or driveway,
    /// water, deck, lamp or sign cell of the roadside mask, and outside every
    /// city race route's run-off (<see cref="RaceRunOff"/>). The clear zone and
    /// the junction's sight triangles and corner spots are exactly where the
    /// mask keeps room for this furniture (RoadsideOccupancy's header), so they
    /// are allowed. A STOP sign stands on the approach's right at its stop
    /// line; a mast-arm pole at the approach's FAR-RIGHT corner - found by
    /// walking the line just outside its right edge on across the junction to
    /// the first clear spot past the node - with its arm out over the inbound
    /// lanes and a head over each (three at most); a span wire between the
    /// far-right corners of an opposing pair, a head hung where each inbound
    /// lane's line crosses it. Decided from global data (the static mask) plus
    /// the tile's own lamps, by the tile the junction's centre is in, which
    /// owns all of it; its feet are marked on the tile's mask before the
    /// poles, signs and trees take theirs.
    ///
    /// PHONES ARE THE BUDGET: a tile's junction furniture is THREE draws
    /// whatever it holds - one static mesh on the kit's signal atlas
    /// (<see cref="CityKit.signals"/>: tools/city/signals_atlas.py, the
    /// owner's pack metal and his Roads pack's STOP sign), one mesh of lenses
    /// on ONE runtime material shared by every tile, whose 4 x 2 texture the
    /// clock rewrites when a phase changes (the lenses ARE its texels), and
    /// one mesh of halos (PSX/Halo, NightGlow's) lit after dark, recoloured
    /// by vertex colour on a phase change. No per-light draw, no per-frame
    /// work between phase changes.
    /// </summary>
    public static class CitySignals
    {
        /// <summary>Tools switch the junction furniture off for an A/B.</summary>
        public static bool Enabled = true;

        // ==================================================================
        //  THE CLOCK
        // ==================================================================

        /// <summary>A phase: green, yellow, then an all-red clearance, for one
        /// pair of opposing approaches, then the same for the other.</summary>
        public const float GreenS = 14f, YellowS = 3.5f, AllRedS = 1.5f;
        public const float HalfCycleS = GreenS + YellowS + AllRedS;
        public const float CycleS = 2f * HalfCycleS;

        public enum Aspect : byte { Red = 0, Yellow = 1, Green = 2 }

        /// <summary>What a phase group (0 or 1) shows at clock time t (seconds).
        /// THE one clock: every head in the city, and later the traffic.</summary>
        public static Aspect AspectOf(int group, double t)
        {
            double u = t % CycleS;
            if (u < 0) u += CycleS;
            if (group != 0) { u -= HalfCycleS; if (u < 0) u += CycleS; }
            if (u < GreenS) return Aspect.Green;
            if (u < GreenS + YellowS) return Aspect.Yellow;
            return Aspect.Red;
        }

        /// <summary>What an approach's head shows now (the later traffic
        /// package's question).</summary>
        public static Aspect AspectNow(Approach a) => AspectOf(a.group, Now);

        /// <summary>The clock's last time (<see cref="Tick"/>).</summary>
        public static double Now { get; private set; }

        static int lastState = -1;
        static bool lastNight;

        /// <summary>
        /// Advance the one clock (CityWorld.Update; a tool calls it once after
        /// standing its tiles up). Rewrites the lens texture and the halos'
        /// colours only when a phase changes or night falls.
        /// </summary>
        public static void Tick(double t)
        {
            Now = t;
            int state = (int)AspectOf(0, t) * 3 + (int)AspectOf(1, t);
            bool night = NightGlow.On;
            if (state == lastState && night == lastNight) return;
            lastState = state; lastNight = night;
            WriteLamps();
            for (int i = halos.Count - 1; i >= 0; i--)
            {
                var h = halos[i];
                if (h.r == null || h.mesh == null) { halos.RemoveAt(i); continue; }
                h.r.enabled = night;
                if (night) Recolour(h);
            }
        }

        // lens colours, lit and dark (sRGB): an LED red, amber, blue-green
        static readonly Color32[] LitColour = { new Color32(255, 36, 24, 255), new Color32(255, 176, 20, 255), new Color32(40, 255, 150, 255) };
        static readonly Color32[] DarkColour = { new Color32(46, 10, 8, 255), new Color32(44, 30, 6, 255), new Color32(6, 36, 24, 255) };
        static readonly Color32 Black = new Color32(0, 0, 0, 255);

        static bool Lit(int group, int colour) => (int)AspectOf(group, Now) == colour;

        static Texture2D lampTex;
        static Material lampMat, haloMat;
        static bool warned;

        static void WriteLamps()
        {
            if (lampTex == null) return;
            var px = new Color32[8];
            for (int g = 0; g < 2; g++)
            {
                for (int c = 0; c < 3; c++) px[g * 4 + c] = Lit(g, c) ? LitColour[c] : DarkColour[c];
                px[g * 4 + 3] = Black;
            }
            lampTex.SetPixels32(px);
            lampTex.Apply(false, false);
        }

        /// <summary>The kit's signal atlas material (null: the furniture is
        /// not drawn; the poles still stand).</summary>
        public static Material StaticMaterial()
        {
            var kit = CityKit.Get();
            return kit != null ? kit.signals : null;
        }

        /// <summary>
        /// THE LENSES' ONE MATERIAL: the kit's signal material without its
        /// atlas keyword, emissive (a lens is its own light, and at night an
        /// emitter the halation glows by), on a 4 x 2 texture - a column per
        /// lens colour, a row per phase group - that <see cref="Tick"/>
        /// rewrites when a phase changes. Every lens in the city samples it.
        /// </summary>
        public static Material LampMaterial()
        {
            if (lampMat != null) return lampMat;
            var baseMat = StaticMaterial();
            if (baseMat == null)
            {
                if (!warned) { warned = true; Debug.LogWarning("[City] the city kit has no signals material - the signal heads are not drawn (run a city build: PSXRacingBuilder.EnsureCityKit)."); }
                return null;
            }
            lampTex = new Texture2D(4, 2, TextureFormat.RGBA32, false)
            {
                name = "CitySignalLamps", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            lampMat = new Material(baseMat) { name = "CitySignalLamps", hideFlags = HideFlags.HideAndDontSave };
            lampMat.DisableKeyword("PSX_FURNITURE");
            lampMat.DisableKeyword("PSX_ATLAS_RECT");
            lampMat.mainTexture = lampTex;
            lampMat.mainTextureScale = Vector2.one;
            lampMat.mainTextureOffset = Vector2.zero;
            lampMat.color = Color.white;
            lampMat.SetFloat("_Emission", 1f);
            PSXTexDecode.Stamp(lampMat);
            lastState = -1;
            WriteLamps();
            return lampMat;
        }

        /// <summary>The halos' material: PSX/Halo (NightGlow's shader), white,
        /// each halo tinted by its vertex colour.</summary>
        public static Material HaloMaterial()
        {
            if (haloMat != null) return haloMat;
            var sh = Shader.Find("PSX/Halo");
            if (sh == null) return null;
            haloMat = new Material(sh) { name = "CitySignalHalos", hideFlags = HideFlags.HideAndDontSave };
            haloMat.SetColor("_Color", Color.white);
            haloMat.SetFloat("_Size", HaloSizeM);
            haloMat.SetFloat("_Strength", 1.3f);
            return haloMat;
        }

        /// <summary>A lens halo's half size (a street lamp's is 2.6 m).</summary>
        public const float HaloSizeM = 1.1f;

        sealed class HaloSet { public Mesh mesh; public Renderer r; public byte[] lens; public Color32[] cols; }
        static readonly List<HaloSet> halos = new List<HaloSet>();

        static void Recolour(HaloSet h)
        {
            for (int i = 0; i < h.lens.Length; i++)
            {
                int g = h.lens[i] >> 2, c = h.lens[i] & 3;
                var col = Lit(g, c) ? LitColour[c] : Black;
                int v = i * 4;
                h.cols[v] = h.cols[v + 1] = h.cols[v + 2] = h.cols[v + 3] = col;
            }
            h.mesh.colors32 = h.cols;
        }

        // ==================================================================
        //  THE JUNCTIONS (decided once a map)
        // ==================================================================

        /// <summary>How far before a junction node a signal tagged on its
        /// approach still marks it.</summary>
        public const float SignalSnapM = 20f;
        /// <summary>Signal nodes joined by an edge this short are one junction.</summary>
        public const float ClusterM = 40f;
        /// <summary>How far before a junction a stop tagged on its approach
        /// still stops that approach.</summary>
        public const float StopSnapM = 30f;
        /// <summary>The stop line: this far back from where the junction's
        /// patch starts.</summary>
        public const float StopBackM = 1.2f;

        /// <summary>One way into a junction: the edge, the node it arrives at,
        /// the stop line's arc position and plan point on the OSM line, the
        /// direction of travel, the inbound lanes' span right of that line
        /// (metres, + = right of travel) and count, the road's height there,
        /// and the phase group (signals).</summary>
        public struct Approach
        {
            public int edge, node;
            public float sStop;
            public Vector2 at, u;
            public float inL, inR;
            public int lanesIn;
            public float roadY;
            public byte group;
            public Vector2 Right => new Vector2(u.y, -u.x);
        }

        public sealed class Junction
        {
            public int id;
            public bool signal, allWay;
            public Vector2 centre;
            public float roadY;
            public int[] nodes;
            public readonly List<Approach> approaches = new List<Approach>();
            public bool uptown;
        }

        static CityMap mapFor;
        static CityMeshes.Trims trimsFor;
        static List<Junction> junctions;
        static Dictionary<long, List<Junction>> byTile;

        /// <summary>Every junction with furniture (the tools' census).</summary>
        public static IReadOnlyList<Junction> All(CityMap map, CityMeshes.Trims trims) { Ensure(map, trims); return junctions; }

        /// <summary>Has this tile a junction to furnish?</summary>
        public static bool AnyIn(CityMap map, CityMeshes.Trims trims, int tx, int tz)
        {
            if (map == null || trims == null) return false;
            Ensure(map, trims);
            return byTile.ContainsKey(Key(tx, tz));
        }

        static long Key(int tx, int tz) => ((long)tx << 24) ^ (tz & 0xFFFFFF);

        /// <summary>Every tile that owns a junction (the census), in a fixed order.</summary>
        public static List<Vector2Int> Tiles(CityMap map, CityMeshes.Trims trims)
        {
            Ensure(map, trims);
            var l = new List<Vector2Int>(byTile.Count);
            foreach (var kv in byTile)
            {
                var j = kv.Value[0];
                l.Add(new Vector2Int(Mathf.FloorToInt(j.centre.x / CityMeshes.TileSize), Mathf.FloorToInt(j.centre.y / CityMeshes.TileSize)));
            }
            l.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            return l;
        }

        /// <summary>charlotte_stops.bytes: stop=all 1, forward 2, backward 4, minor 8.</summary>
        static Dictionary<ulong, byte> LoadStopFlags()
        {
            var d = new Dictionary<ulong, byte>();
            var ta = Resources.Load<TextAsset>("charlotte_stops");
            if (ta == null) { Debug.LogWarning("[City] Resources/charlotte_stops.bytes missing (node tools/city/stop_tags.mjs) - stops are read from the graph alone."); return d; }
            var b = ta.bytes;
            if (b.Length < 12 || b[0] != 'P' || b[1] != 'S' || b[2] != 'T' || b[3] != 'P') { Debug.LogError("[City] charlotte_stops.bytes: bad magic"); return d; }
            int n = System.BitConverter.ToInt32(b, 8);
            for (int i = 0; i < n && 12 + i * 9 + 9 <= b.Length; i++)
            {
                int p = 12 + i * 9;
                ulong id = System.BitConverter.ToUInt32(b, p) | ((ulong)System.BitConverter.ToUInt32(b, p + 4) << 32);
                d[id] = b[p + 8];
            }
            Resources.UnloadAsset(ta);
            return d;
        }

        static int Degree(CityMap map, int n)
        {
            int k = 0;
            foreach (int ei in map.nodeEdges[n]) { var e = map.edges[ei]; if (e.a != e.b) k++; }
            return k;
        }

        static void Ensure(CityMap map, CityMeshes.Trims trims)
        {
            if (mapFor == map && trimsFor == trims && junctions != null) return;
            mapFor = map; trimsFor = trims;
            junctions = new List<Junction>();
            byTile = new Dictionary<long, List<Junction>>();
            if (map == null || map.tagged == null) return;
            int nn = map.nodes.Length;

            // ---- signals: the nodes they mark
            var sig = new bool[nn];
            if (map.nodeControl != null)
                for (int i = 0; i < nn; i++) if (map.nodeControl[i] == 4) sig[i] = true;
            foreach (var t in map.tagged)
            {
                if (t.kind != 4 || t.edge < 0 || t.edge >= map.edges.Length) continue;
                var e = map.edges[t.edge];
                float d0 = t.s, d1 = e.length - t.s;
                if (Mathf.Min(d0, d1) > SignalSnapM) continue;    // mid-block: a crossing's, not a junction's
                sig[d0 <= d1 ? e.a : e.b] = true;
            }
            for (int i = 0; i < nn; i++) if (sig[i] && Degree(map, i) < 3) sig[i] = false;

            // ---- one junction per cluster of marked nodes
            var parent = new int[nn];
            for (int i = 0; i < nn; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[System.Math.Max(a, b)] = System.Math.Min(a, b); }
            var member = new bool[nn];
            for (int i = 0; i < nn; i++)
            {
                if (!sig[i]) continue;
                member[i] = true;
                foreach (int ei in map.nodeEdges[i])
                {
                    var e = map.edges[ei];
                    if (e.a == e.b || e.length > ClusterM) continue;
                    int o = e.a == i ? e.b : e.a;
                    if (sig[o]) Union(i, o);
                    else if (e.length <= 30f && Degree(map, o) >= 3 && !(e.cls >= 5 && !e.link)) { member[o] = true; Union(i, o); }
                }
            }
            var clusters = new Dictionary<int, List<int>>();
            for (int i = 0; i < nn; i++)
            {
                if (!member[i]) continue;
                int r = Find(i);
                if (!clusters.TryGetValue(r, out var l)) clusters[r] = l = new List<int>(4);
                l.Add(i);
            }
            var inCluster = new int[nn];
            for (int i = 0; i < nn; i++) inCluster[i] = -1;
            foreach (var kv in clusters) foreach (int n in kv.Value) inCluster[n] = kv.Key;

            var keys = new List<int>(clusters.Keys);
            keys.Sort();
            foreach (int root in keys)
            {
                var ns = clusters[root];
                var j = new Junction { signal = true, nodes = ns.ToArray() };
                int arms = 0;
                foreach (int n in ns)
                    foreach (int ei in map.nodeEdges[n])
                    {
                        var e = map.edges[ei];
                        if (e.a == e.b) continue;
                        int o = e.a == n ? e.b : e.a;
                        if (inCluster[o] == root) continue;
                        arms++;
                        if (MakeApproach(map, trims, e, n, out var a)) j.approaches.Add(a);
                    }
                if (arms < 3 || j.approaches.Count < 2) continue;
                Finish(map, j, root);
            }

            // ---- stops
            var flags = LoadStopFlags();
            var stopAt = new Dictionary<int, Junction>();
            var seen = new HashSet<long>();
            void AddStop(int n, CityMap.Edge e, bool allWay)
            {
                if (inCluster[n] >= 0 || sig[n]) return;              // a signal wins
                if (!seen.Add(((long)e.index << 1) | (e.b == n ? 1L : 0L))) return;
                if (!MakeApproach(map, trims, e, n, out var a)) return;
                if (!stopAt.TryGetValue(n, out var j)) stopAt[n] = j = new Junction { signal = false, nodes = new[] { n } };
                j.approaches.Add(a);
                j.allWay |= allWay;
            }
            foreach (var t in map.tagged)
            {
                if (t.kind != 2 || t.edge < 0 || t.edge >= map.edges.Length) continue;
                var e = map.edges[t.edge];
                flags.TryGetValue(t.nodeId, out byte f);
                bool fwd = (f & 2) != 0, back = (f & 4) != 0;
                float d0 = t.s, d1 = e.length - t.s;
                if (Mathf.Min(d0, d1) < 1.5f)
                {
                    // AT a junction node
                    int n = d0 <= d1 ? e.a : e.b;
                    if (Degree(map, n) < 3) continue;
                    if ((f & 1) != 0)
                    {
                        foreach (int ei in map.nodeEdges[n]) AddStop(n, map.edges[ei], true);
                        continue;
                    }
                    if (fwd || back)
                    {
                        // along the way's own direction (the edges keep its order)
                        foreach (int ei in map.nodeEdges[n])
                        {
                            var o = map.edges[ei];
                            if (o.wayId != t.wayId) continue;
                            if ((fwd && o.b == n) || (back && o.a == n)) AddStop(n, o, false);
                        }
                        continue;
                    }
                    // the node's minor road stops (two of one class: both do)
                    int minCls = int.MaxValue, wayCls = int.MaxValue;
                    foreach (int ei in map.nodeEdges[n])
                    {
                        var o = map.edges[ei];
                        minCls = Mathf.Min(minCls, o.cls);
                        if (o.wayId == t.wayId) wayCls = Mathf.Min(wayCls, o.cls);
                    }
                    if (wayCls > minCls) continue;
                    foreach (int ei in map.nodeEdges[n])
                    {
                        var o = map.edges[ei];
                        if (o.wayId == t.wayId) AddStop(n, o, false);
                    }
                }
                else if (Mathf.Min(d0, d1) <= StopSnapM)
                {
                    // on an approach, just before its junction
                    int n = d0 <= d1 ? e.a : e.b;
                    if (Degree(map, n) < 3) continue;
                    if (fwd && n != e.b) continue;
                    if (back && n != e.a) continue;
                    AddStop(n, e, false);
                }
            }
            var stopKeys = new List<int>(stopAt.Keys);
            stopKeys.Sort();
            foreach (int n in stopKeys) Finish(map, stopAt[n], n);

            int sigs = 0, sigApp = 0, stops = 0, stopApp = 0;
            foreach (var j in junctions) { if (j.signal) { sigs++; sigApp += j.approaches.Count; } else { stops++; stopApp += j.approaches.Count; } }
            Debug.Log($"[City] junction controls: {sigs} signalised junctions ({sigApp} approaches), {stops} stop-controlled ({stopApp} approaches) from {map.tagged.Length} tagged nodes, {flags.Count} stop tags");
        }

        /// <summary>The approach an edge makes into node n, if it makes one.</summary>
        static bool MakeApproach(CityMap map, CityMeshes.Trims trims, CityMap.Edge e, int n, out Approach a)
        {
            a = default;
            if (e.a == e.b || e.tunnel || e.roundabout) return false;
            if (e.cls >= 5 && !e.link) return false;                  // no freeway mainline
            if (e.oneway && e.a == n) return false;                   // it leaves the junction
            if (e.length < 6f) return false;
            bool atB = e.b == n;
            float trim = trims.TrimAt(e, n);
            float sMouth = atB ? e.length - trim : trim;
            float sStop = atB ? sMouth - StopBackM : sMouth + StopBackM;
            if (sStop < 1f || sStop > e.length - 1f) return false;
            if (e.ElevatedAt(sStop)) return false;
            var tan = e.TangentAt(sStop);
            var u = atB ? tan : -tan;
            int sideR = atB ? 1 : -1;                                 // travel-right as a side of the points
            float edgeR = e.PaveEdgeM(sStop, sideR), edgeL = e.PaveEdgeM(sStop, -sideR);
            float inL, inR = edgeR - 0.3f;
            int lanesIn;
            if (e.oneway) { inL = -edgeL + 0.3f; lanesIn = e.lanes; }
            else
            {
                // the lanes' centre (lmOff: + = left of a->b), in travel-right metres
                float divider = atB ? -e.lmOff : e.lmOff;
                inL = divider + (e.turnLane ? 1.7f : 0.15f);
                lanesIn = Mathf.Max(1, (e.lanes - (e.turnLane ? 1 : 0)) / 2);
            }
            if (inR - inL < 2.4f) inL = inR - 2.4f;
            a = new Approach
            {
                edge = e.index, node = n, sStop = sStop, at = e.PointAt(sStop), u = u,
                inL = inL, inR = inR, lanesIn = Mathf.Clamp(lanesIn, 1, 6), roadY = e.YAt(sStop),
            };
            return true;
        }

        static void Finish(CityMap map, Junction j, int seed)
        {
            j.id = junctions.Count;
            var c = Vector2.zero; float y = float.MinValue;
            foreach (int n in j.nodes) { c += map.nodes[n]; y = Mathf.Max(y, map.nodeY[n]); }
            j.centre = c / j.nodes.Length;
            foreach (var a in j.approaches) y = Mathf.Max(y, a.roadY);
            j.roadY = y;
            j.uptown = CityPoles.Uptown(map, j.centre);
            if (j.signal)
            {
                // the main road: the biggest approach; within 45 degrees of it
                // is its pair, the rest the other; half start on the other
                int best = 0;
                for (int i = 1; i < j.approaches.Count; i++)
                {
                    var ei = map.edges[j.approaches[i].edge]; var eb = map.edges[j.approaches[best].edge];
                    if (ei.cls * 16 + ei.lanes > eb.cls * 16 + eb.lanes) best = i;
                }
                var axis = j.approaches[best].u;
                byte flip = (byte)(Hash01(seed, 0, 41) < 0.5f ? 1 : 0);
                for (int i = 0; i < j.approaches.Count; i++)
                {
                    var a = j.approaches[i];
                    a.group = (byte)((Mathf.Abs(Vector2.Dot(a.u, axis)) >= 0.7071f ? 0 : 1) ^ flip);
                    j.approaches[i] = a;
                }
            }
            junctions.Add(j);
            long k = Key(Mathf.FloorToInt(j.centre.x / CityMeshes.TileSize), Mathf.FloorToInt(j.centre.y / CityMeshes.TileSize));
            if (!byTile.TryGetValue(k, out var l)) byTile[k] = l = new List<Junction>(4);
            l.Add(j);
        }

        // ==================================================================
        //  A TILE
        // ==================================================================

        public struct Head { public Vector3 pos; public Vector2 face; public byte group; }
        public struct Stop { public Vector3 foot; public Vector2 face; public int edge; }

        public sealed class SignalTile
        {
            public int tx, tz;
            public Mesh mesh, lamps, halos;
            /// <summary>Signal poles (solid), world: foot and top.</summary>
            public readonly List<(Vector3 foot, float top)> poles = new List<(Vector3, float)>();
            public readonly List<Head> heads = new List<Head>();
            public readonly List<Stop> stops = new List<Stop>();
            public int junctions, signalJunctions, mastArms, spanWires, stopBars, refused;
            /// <summary>The halos' lens codes (group << 2 | colour), in mesh order.</summary>
            public byte[] haloLens;
            public float ms;
        }

        const float PoleW = 0.32f, ArmW = 0.16f, MaxArmM = 14.5f;
        const float HeadW = 0.36f, HeadH = 1.06f, HeadD = 0.26f, LensM = 0.25f, LensStep = 0.34f;
        const float PlateW = 0.62f, PlateH = 1.30f;
        const float HeadClearM = 4.9f;          // the bottom of a head over the road (MUTCD 4.6-5.8 m)
        const float SignM = 0.76f, SignBottomM = 2.1f, PostW = 0.07f;
        const float BarM = 0.45f;
        public const float FootSinkM = 0.3f;
        public const float PoleColliderW = 0.36f;

        public static readonly Color32 CellMetal = new Color32(0, 0, 127, 255), CellBlack = new Color32(128, 0, 127, 255),
                                       CellStop = new Color32(0, 128, 127, 255), CellPaint = new Color32(128, 128, 127, 255);

        static CityMap mapNow;
        static CityMeshes.Trims trimsNow;
        static Dictionary<long, List<CityBuildings.B>> buildingsNow;
        static RoadsideOccupancy occNow;
        static Vector2 tileMin, tileMax;

        /// <summary>
        /// A tile's junction furniture and its three meshes (world space moved
        /// to the tile's origin). Marks every foot on <paramref name="occ"/>.
        /// Deterministic: the same junctions, whichever tile asks first.
        /// </summary>
        public static SignalTile Build(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings,
                                       CityMeshes.TileMeshes tm, RoadsideOccupancy occ, int tx, int tz)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var st = new SignalTile { tx = tx, tz = tz };
            if (!Enabled || map == null) return st;
            Ensure(map, trims);
            if (!byTile.TryGetValue(Key(tx, tz), out var list)) return st;
            mapNow = map; trimsNow = trims; buildingsNow = buildings; occNow = occ;
            float ts = CityMeshes.TileSize;
            tileMin = new Vector2(tx * ts, tz * ts); tileMax = tileMin + Vector2.one * ts;
            Begin();
            try
            {
                foreach (var j in list) Furnish(j, st);
            }
            finally { occNow = null; }
            var origin = tm != null ? tm.origin : new Vector3(tileMin.x, 0f, tileMin.y);
            st.mesh = End(origin, "junctionFurniture");
            st.lamps = EndLamps(origin);
            st.halos = EndHalos(origin, out st.haloLens);
            st.ms = (float)clock.Elapsed.TotalMilliseconds;
            return st;
        }

        static void Furnish(Junction j, SignalTile st)
        {
            st.junctions++;
            // the stop bars, every approach
            foreach (var a in j.approaches) { StopBar(a); st.stopBars++; }
            if (!j.signal)
            {
                foreach (var a in j.approaches)
                    if (PlaceStop(a, out var s)) { st.stops.Add(s); EmitStopSign(s, a); }
                    else st.refused++;
                return;
            }
            st.signalJunctions++;
            // each approach's far-right corner
            var corner = new Vector2?[j.approaches.Count];
            for (int i = 0; i < j.approaches.Count; i++)
                if (FarCorner(j, j.approaches[i], out var f)) corner[i] = f;
            // outside uptown: a span wire between an opposing pair's corners
            if (!j.uptown && SpanWire(j, corner, st)) { st.spanWires++; return; }
            for (int i = 0; i < j.approaches.Count; i++)
            {
                if (corner[i] == null) { st.refused++; continue; }
                if (MastArm(j, j.approaches[i], corner[i].Value, st)) st.mastArms++;
                else st.refused++;
            }
        }

        // ---- where a foot may stand ---------------------------------------

        const byte Hard = RoadsideOccupancy.Building | RoadsideOccupancy.Water | RoadsideOccupancy.Deck | RoadsideOccupancy.Other;

        static bool InTile(Vector2 p) => p.x >= tileMin.x && p.y >= tileMin.y && p.x < tileMax.x && p.y < tileMax.y;

        /// <summary>Clear of every lane (past the drawn pavement of every road
        /// near it by <paramref name="offM"/>), of buildings, lots and their
        /// driveways, water, decks, lamps and signs (the mask), and of every
        /// race run-off.</summary>
        static bool FootOk(Vector2 f, float offM, float r)
        {
            var map = mapNow;
            int tx = Mathf.FloorToInt(f.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(f.y / CityMeshes.TileSize);
            var sm = RoadsideOccupancy.Static(map, trimsNow, buildingsNow, tx, tz);
            if ((sm.At(f) & Hard) != 0) return false;
            if (occNow != null && InTile(f) && (occNow.At(f) & Hard) != 0) return false;
            if (RaceRunOff.Inside(map, trimsNow, f)) return false;
            float d = sm.RoadEdgeDistance(f, offM + r + 2f, out _, out _, out _);
            if (d < offM + r) return false;
            // the ground not too far off the road's (no pole down a bank)
            return true;
        }

        static float Ground(Vector2 f) => CityMeshes.LatticeAt(mapNow, f.x, f.y);

        static void Claim(Vector2 f, float r)
        {
            if (occNow != null) occNow.MarkDisc(f, r + RoadsideOccupancy.CellPadM, RoadsideOccupancy.Other);
        }

        // ---- STOP ---------------------------------------------------------

        static readonly float[] StopOut = { 0.9f, 1.4f, 2.0f, 2.8f };
        static readonly float[] StopAlong = { 0f, -1f, -2.5f, 0.8f, -4f };

        static bool PlaceStop(Approach a, out Stop s)
        {
            s = default;
            var r = a.Right;
            foreach (float o in StopOut)
                foreach (float t in StopAlong)
                {
                    var f = a.at + r * (a.inR + 0.3f + o) + a.u * t;
                    if (!FootOk(f, 0.6f, 0.3f)) continue;
                    float g = Ground(f);
                    if (Mathf.Abs(g - a.roadY) > 2.5f) continue;
                    Claim(f, 0.6f);
                    s = new Stop { foot = new Vector3(f.x, g, f.y), face = -a.u, edge = a.edge };
                    return true;
                }
            return false;
        }

        static void EmitStopSign(Stop s, Approach a)
        {
            var face = new Vector3(s.face.x, 0f, s.face.y);
            var across = new Vector3(-s.face.y, 0f, s.face.x);
            float cy = Mathf.Max(a.roadY, s.foot.y) + SignBottomM + SignM * 0.5f;
            var post = new Vector3(s.foot.x, 0f, s.foot.z);
            float y0 = s.foot.y - FootSinkM, y1 = cy + SignM * 0.4f;
            Box(new Vector3(post.x, (y0 + y1) * 0.5f, post.z), across * (PostW * 0.5f), Vector3.up * ((y1 - y0) * 0.5f), face * (PostW * 0.5f), CellMetal, true);
            var c = new Vector3(post.x, cy, post.z) + face * (PostW * 0.5f + 0.02f);
            Octagon(c, face, across, SignM, CellStop, true);
            Octagon(c - face * 0.012f, -face, -across, SignM, CellMetal, false);
        }

        /// <summary>A flat-topped octagon <paramref name="flat"/> across its
        /// flats, facing <paramref name="n"/>; the sign's own texture spans the
        /// cell (<paramref name="face"/>) or its metal tiles in metres.</summary>
        static void Octagon(Vector3 c, Vector3 n, Vector3 right, float flat, Color32 cell, bool face)
        {
            int i0 = vs.Count;
            float R = flat * 0.5f / Mathf.Cos(22.5f * Mathf.Deg2Rad);
            vs.Add(c); ns.Add(n); cols.Add(cell); uvs.Add(face ? new Vector2(0.5f, 0.5f) : new Vector2(0.4f, 0.4f)); wire.Add(Vector2.zero);
            for (int k = 0; k < 8; k++)
            {
                float ang = (22.5f + 45f * k) * Mathf.Deg2Rad;
                float x = Mathf.Cos(ang) * R, y = Mathf.Sin(ang) * R;
                vs.Add(c + right * x + Vector3.up * y); ns.Add(n); cols.Add(cell); wire.Add(Vector2.zero);
                uvs.Add(face ? new Vector2(0.5f + x / flat * 0.996f, 0.5f + y / flat * 0.996f) : new Vector2(0.4f + x, 0.4f + y));
            }
            for (int k = 0; k < 8; k++)
            {
                int a = i0 + 1 + k, b = i0 + 1 + (k + 1) % 8;
                if (Vector3.Dot(Vector3.Cross(vs[a] - vs[i0], vs[b] - vs[i0]), n) > 0f) { tris.Add(i0); tris.Add(a); tris.Add(b); }
                else { tris.Add(i0); tris.Add(b); tris.Add(a); }
            }
        }

        // ---- the stop bar ---------------------------------------------------

        static void StopBar(Approach a)
        {
            var r = a.Right;
            float y = a.roadY + 0.03f;
            float w = a.inR - a.inL;
            int n = Mathf.Max(1, Mathf.CeilToInt(w / 2f));
            var u3 = new Vector3(a.u.x, 0f, a.u.y);
            int i0 = vs.Count;
            for (int k = 0; k <= n; k++)
            {
                float o = a.inL + w * k / n;
                var p = a.at + r * o;
                for (int s = 0; s < 2; s++)
                {
                    var q = new Vector3(p.x, y, p.y) + u3 * ((s == 0 ? -0.5f : 0.5f) * BarM);
                    vs.Add(q); ns.Add(Vector3.up); cols.Add(CellPaint); wire.Add(Vector2.zero);
                    uvs.Add(new Vector2(o, s * BarM));
                }
            }
            for (int k = 0; k < n; k++)
            {
                int A = i0 + k * 2, B = A + 1, C = A + 3, D = A + 2;
                // facing up: (A, B, C) with B toward +u and C across
                if (Vector3.Dot(Vector3.Cross(vs[B] - vs[A], vs[C] - vs[A]), Vector3.up) > 0f)
                { tris.Add(A); tris.Add(B); tris.Add(C); tris.Add(A); tris.Add(C); tris.Add(D); }
                else
                { tris.Add(A); tris.Add(C); tris.Add(B); tris.Add(A); tris.Add(D); tris.Add(C); }
            }
        }

        // ---- signals ---------------------------------------------------------

        /// <summary>The approach's far-right corner: along the line just
        /// outside its right pavement edge, on across the junction, the first
        /// clear spot past its node.</summary>
        static bool FarCorner(Junction j, Approach a, out Vector2 foot)
        {
            foot = default;
            var r = a.Right;
            var node = mapNow.nodes[a.node];
            float t0 = Vector2.Dot(node - a.at, a.u) + 2f;
            for (float extra = 1.2f; extra <= 4.8f; extra += 1.2f)
                for (float t = t0; t <= t0 + 40f; t += 1.5f)
                {
                    var f = a.at + r * (a.inR + 0.3f + extra) + a.u * t;
                    if (!FootOk(f, 1.0f, PoleW * 0.5f)) continue;
                    if (Mathf.Abs(Ground(f) - a.roadY) > 3f) continue;
                    foot = f;
                    return true;
                }
            return false;
        }

        /// <summary>Plan positions of up to three heads over an approach's
        /// inbound lanes, as lateral offsets right of its line.</summary>
        static void LaneOffsets(Approach a, List<float> into)
        {
            into.Clear();
            int n = a.lanesIn;
            float w = (a.inR - a.inL) / n;
            if (n <= 3) for (int k = 0; k < n; k++) into.Add(a.inL + w * (k + 0.5f));
            else { into.Add(a.inL + w * 0.5f); into.Add(a.inL + w * (n * 0.5f)); into.Add(a.inR - w * 0.5f); }
        }
        static readonly List<float> laneScratch = new List<float>(4);

        static bool MastArm(Junction j, Approach a, Vector2 foot, SignalTile st)
        {
            var r = a.Right;
            float latF = Vector2.Dot(foot - a.at, r);
            LaneOffsets(a, laneScratch);
            float armY = j.roadY + HeadClearM + HeadH + 0.2f;
            float g = Ground(foot);
            var heads = new List<Vector2>(3);
            float reach = 0f;
            foreach (float o in laneScratch)
            {
                float d = latF - o;
                if (d < 0.8f || d > MaxArmM - 0.3f) continue;
                heads.Add(foot - r * d);
                reach = Mathf.Max(reach, d);
            }
            if (heads.Count == 0) return false;
            Claim(foot, PoleW);
            float top = armY + 0.6f;
            var f3 = new Vector3(foot.x, g - FootSinkM, foot.y);
            var u3 = new Vector3(a.u.x, 0f, a.u.y); var r3 = new Vector3(r.x, 0f, r.y);
            Box(new Vector3(foot.x, (f3.y + top) * 0.5f, foot.y), r3 * (PoleW * 0.5f), Vector3.up * ((top - f3.y) * 0.5f), u3 * (PoleW * 0.5f), CellMetal, true);
            st.poles.Add((f3, top));
            // the arm, out over the lanes
            float armL = reach + 0.4f;
            var armMid = new Vector3(foot.x, armY, foot.y) - r3 * (armL * 0.5f);
            Box(armMid, r3 * (armL * 0.5f), Vector3.up * (ArmW * 0.5f), u3 * (ArmW * 0.5f), CellMetal, false);
            foreach (var h in heads)
            {
                var hc = new Vector3(h.x, armY - ArmW * 0.5f - 0.08f - HeadH * 0.5f, h.y);
                Box(new Vector3(h.x, armY - ArmW * 0.5f - 0.04f, h.y), r3 * 0.03f, Vector3.up * 0.05f, u3 * 0.03f, CellBlack, true);
                EmitHead(hc, a, st);
            }
            return true;
        }

        /// <summary>A span wire between the far-right corners of an opposing
        /// pair, a head hung over each inbound lane of every approach where
        /// its line crosses the wire. False (nothing emitted) where no pair
        /// strings one that every approach can hang a head from.</summary>
        static bool SpanWire(Junction j, Vector2?[] corner, SignalTile st)
        {
            int n = j.approaches.Count;
            for (int i = 0; i < n; i++)
                for (int k = i + 1; k < n; k++)
                {
                    if (corner[i] == null || corner[k] == null) continue;
                    if (Vector2.Dot(j.approaches[i].u, j.approaches[k].u) > -0.85f) continue;
                    Vector2 A = corner[i].Value, B = corner[k].Value;
                    float span = Vector2.Distance(A, B);
                    if (span < 10f || span > 70f) continue;
                    // every approach must hang at least one head from it
                    var hangs = new List<(Vector2 p, float t, int app)>();
                    bool all = true;
                    for (int q = 0; q < n && all; q++)
                    {
                        var a = j.approaches[q];
                        LaneOffsets(a, laneScratch);
                        int got = 0;
                        foreach (float o in laneScratch)
                        {
                            var p0 = a.at + a.Right * o;
                            if (!Cross(p0, a.u, A, B, out float along, out float t)) continue;
                            if (along < 1f || t < 0.08f || t > 0.92f) continue;
                            hangs.Add((Vector2.Lerp(A, B, t), t, q));
                            got++;
                        }
                        if (got == 0) all = false;
                    }
                    if (!all) continue;

                    float sag = 0.025f * span;
                    float attach = j.roadY + HeadClearM + HeadH + 0.35f + sag + 0.2f;
                    float top = attach + 0.45f;
                    foreach (var P in new[] { A, B })
                    {
                        float g = Ground(P);
                        var f3 = new Vector3(P.x, g - FootSinkM, P.y);
                        Box(new Vector3(P.x, (f3.y + top) * 0.5f, P.y), Vector3.right * (PoleW * 0.5f), Vector3.up * ((top - f3.y) * 0.5f), Vector3.forward * (PoleW * 0.5f), CellMetal, true);
                        st.poles.Add((f3, top));
                        Claim(P, PoleW);
                    }
                    var wa = new Vector3(A.x, attach, A.y); var wb = new Vector3(B.x, attach, B.y);
                    Wire(wa, wb, sag, 0.012f);
                    foreach (var h in hangs)
                    {
                        var a = j.approaches[h.app];
                        var onWire = Vector3.Lerp(wa, wb, h.t) + Vector3.down * (4f * sag * h.t * (1f - h.t));
                        float hangTop = onWire.y - 0.02f, headTop = onWire.y - 0.3f;
                        Box(new Vector3(onWire.x, (hangTop + headTop) * 0.5f, onWire.z), Vector3.right * 0.02f, Vector3.up * ((hangTop - headTop) * 0.5f), Vector3.forward * 0.02f, CellBlack, true);
                        EmitHead(new Vector3(onWire.x, headTop - HeadH * 0.5f, onWire.z), a, st);
                    }
                    return true;
                }
            return false;
        }

        /// <summary>Where the line p0 + u*along crosses the segment A..B
        /// (t along it); false if parallel.</summary>
        static bool Cross(Vector2 p0, Vector2 u, Vector2 A, Vector2 B, out float along, out float t)
        {
            var d = B - A;
            float den = u.x * d.y - u.y * d.x;
            along = t = 0f;
            if (Mathf.Abs(den) < 1e-4f) return false;
            var w = A - p0;
            along = (w.x * d.y - w.y * d.x) / den;
            t = (w.x * u.y - w.y * u.x) / den;
            return true;
        }

        /// <summary>A three-section head at <paramref name="c"/> facing the
        /// approach's traffic: housing, backplate and visors on the atlas,
        /// the three lenses on the lamp mesh, a halo in front of each.</summary>
        static void EmitHead(Vector3 c, Approach a, SignalTile st)
        {
            var face = new Vector3(-a.u.x, 0f, -a.u.y);            // toward the drivers
            var across = new Vector3(-face.z, 0f, face.x);
            Box(c, across * (HeadW * 0.5f), Vector3.up * (HeadH * 0.5f), face * (HeadD * 0.5f), CellBlack, false);
            // the backplate
            Box(c - face * (HeadD * 0.5f + 0.015f), across * (PlateW * 0.5f), Vector3.up * (PlateH * 0.5f), face * 0.015f, CellBlack, false);
            for (int k = 0; k < 3; k++)
            {
                // red on top, yellow, green at the bottom
                float dy = (1 - k) * LensStep;
                var lc = c + Vector3.up * dy + face * (HeadD * 0.5f + 0.012f);
                // its visor
                Box(c + Vector3.up * (dy + LensM * 0.5f + 0.02f) + face * (HeadD * 0.5f + 0.09f), across * (LensM * 0.6f), Vector3.up * 0.015f, face * 0.09f, CellBlack, true);
                LensQuad(lc, face, across, a.group, k);
                haloCentres.Add(lc + face * 0.08f);
                haloLens.Add((byte)(a.group << 2 | LensCode(k)));
                haloYaw.Add(Mathf.Atan2(face.x, face.z));
            }
            st.heads.Add(new Head { pos = c, face = new Vector2(face.x, face.z), group = a.group });
        }

        /// <summary>The texture column of lens k (0 top = red, 1 yellow, 2 green).</summary>
        static int LensCode(int k) => k == 0 ? (int)Aspect.Red : k == 1 ? (int)Aspect.Yellow : (int)Aspect.Green;

        static void LensQuad(Vector3 c, Vector3 n, Vector3 across, int group, int k)
        {
            var uv = new Vector2((LensCode(k) + 0.5f) / 4f, (group + 0.5f) / 2f);
            var u = across * (LensM * 0.5f); var v = Vector3.up * (LensM * 0.5f);
            int i = lv.Count;
            lv.Add(c - u - v); lv.Add(c - u + v); lv.Add(c + u + v); lv.Add(c + u - v);
            for (int q = 0; q < 4; q++) { ln.Add(n); luv.Add(uv); }
            if (Vector3.Dot(Vector3.Cross(lv[i + 1] - lv[i], lv[i + 2] - lv[i]), n) > 0f)
            { lt.Add(i); lt.Add(i + 1); lt.Add(i + 2); lt.Add(i); lt.Add(i + 2); lt.Add(i + 3); }
            else
            { lt.Add(i); lt.Add(i + 2); lt.Add(i + 1); lt.Add(i); lt.Add(i + 3); lt.Add(i + 2); }
        }

        // ==================================================================
        //  THE MESHES (world space here; End moves them to the tile's origin)
        // ==================================================================

        static readonly List<Vector3> vs = new List<Vector3>(2048), ns = new List<Vector3>(2048);
        static readonly List<Vector2> uvs = new List<Vector2>(2048), wire = new List<Vector2>(2048);
        static readonly List<Color32> cols = new List<Color32>(2048);
        static readonly List<int> tris = new List<int>(4096);
        static readonly List<Vector3> lv = new List<Vector3>(512), ln = new List<Vector3>(512);
        static readonly List<Vector2> luv = new List<Vector2>(512);
        static readonly List<int> lt = new List<int>(768);
        static readonly List<Vector3> haloCentres = new List<Vector3>(256);
        static readonly List<byte> haloLens = new List<byte>(256);
        static readonly List<float> haloYaw = new List<float>(256);

        static void Begin()
        {
            vs.Clear(); ns.Clear(); uvs.Clear(); wire.Clear(); cols.Clear(); tris.Clear();
            lv.Clear(); ln.Clear(); luv.Clear(); lt.Clear(); haloCentres.Clear(); haloLens.Clear(); haloYaw.Clear();
        }

        static Mesh End(Vector3 origin, string name)
        {
            if (vs.Count == 0) return null;
            for (int i = 0; i < vs.Count; i++) vs[i] -= origin;
            var m = new Mesh { name = name };
            if (vs.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(vs); m.SetNormals(ns); m.SetUVs(0, uvs); m.SetUVs(1, wire); m.SetColors(cols);
            m.SetTriangles(tris, 0, false);
            m.RecalculateBounds();
            var b = m.bounds; b.Expand(2f); m.bounds = b;   // a span wire is stood up in the shader
            return m;
        }

        static Mesh EndLamps(Vector3 origin)
        {
            if (lv.Count == 0) return null;
            for (int i = 0; i < lv.Count; i++) lv[i] -= origin;
            var m = new Mesh { name = "signalLenses" };
            m.SetVertices(lv); m.SetNormals(ln); m.SetUVs(0, luv);
            var white = new Color32[lv.Count];
            for (int i = 0; i < white.Length; i++) white[i] = new Color32(255, 255, 255, 255);
            m.colors32 = white;
            m.SetTriangles(lt, 0, false);
            m.RecalculateBounds();
            return m;
        }

        static Mesh EndHalos(Vector3 origin, out byte[] codes)
        {
            codes = null;
            if (haloCentres.Count == 0) return null;
            var local = new List<Vector3>(haloCentres.Count);
            foreach (var c in haloCentres) local.Add(c - origin);
            var m = NightGlow.BuildHaloMesh(local, 1f);
            m.name = "signalHalos";
            // each halo faces its lens's way (PSX/Halo: uv1.y = 10 + yaw):
            // nothing glows out of the back of a head
            var uv1 = new Vector2[m.vertexCount];
            for (int i = 0; i < haloYaw.Count; i++)
                uv1[i * 4] = uv1[i * 4 + 1] = uv1[i * 4 + 2] = uv1[i * 4 + 3] = new Vector2(1f, 10f + haloYaw[i]);
            m.uv2 = uv1;
            codes = haloLens.ToArray();
            return m;
        }

        /// <summary>One face of a box: centre, outward normal and the two half
        /// axes across it; UVs in metres, wrapped in the cell.</summary>
        static void Face(Vector3 c, Vector3 n, Vector3 u, Vector3 v, Color32 cell)
        {
            int i = vs.Count;
            vs.Add(c - u - v); vs.Add(c - u + v); vs.Add(c + u + v); vs.Add(c + u - v);
            for (int k = 0; k < 4; k++) { ns.Add(n); cols.Add(cell); wire.Add(Vector2.zero); }
            float lu = 2f * u.magnitude, lvv = 2f * v.magnitude;
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0f, lvv)); uvs.Add(new Vector2(lu, lvv)); uvs.Add(new Vector2(lu, 0f));
            if (Vector3.Dot(Vector3.Cross(vs[i + 1] - vs[i], vs[i + 2] - vs[i]), n) > 0f)
            { tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3); }
            else
            { tris.Add(i); tris.Add(i + 2); tris.Add(i + 1); tris.Add(i); tris.Add(i + 3); tris.Add(i + 2); }
        }

        static void Box(Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, Color32 cell, bool noBottom)
        {
            Face(c + ax, ax.normalized, az, ay, cell);
            Face(c - ax, -ax.normalized, az, ay, cell);
            Face(c + az, az.normalized, ax, ay, cell);
            Face(c - az, -az.normalized, ax, ay, cell);
            Face(c + ay, Vector3.up, ax, az, cell);
            if (!noBottom) Face(c - ay, Vector3.down, ax, az, cell);
        }

        const int WireSegments = 8;

        /// <summary>A wire from a to b sagging <paramref name="sag"/> at its
        /// middle, drawn as CityPoles draws its wires (PSX_FURNITURE stands the
        /// ribbon up square to the eye, a pixel wide at least).</summary>
        static void Wire(Vector3 a, Vector3 b, float sag, float halfW)
        {
            int start = vs.Count;
            if ((b - a).sqrMagnitude < 1e-4f) return;
            for (int i = 0; i <= WireSegments; i++)
            {
                float t = i / (float)WireSegments;
                var q = Vector3.Lerp(a, b, t) + Vector3.down * (4f * sag * t * (1f - t));
                var tan = ((b - a) + Vector3.down * (4f * sag * (1f - 2f * t))).normalized;
                for (int s = -1; s <= 1; s += 2)
                {
                    vs.Add(q); ns.Add(tan); cols.Add(CellBlack); uvs.Add(new Vector2(t, s > 0 ? 1f : 0f));
                    wire.Add(new Vector2(s, halfW));
                }
            }
            for (int i = 0; i < WireSegments; i++)
            {
                int A = start + i * 2, B = A + 1, C = A + 3, D = A + 2;
                tris.Add(A); tris.Add(B); tris.Add(C);
                tris.Add(A); tris.Add(C); tris.Add(D);
            }
        }

        // ==================================================================
        //  STANDING IT UP (CityWorld.PlantTrees)
        // ==================================================================

        /// <summary>
        /// Stand a tile's junction furniture up under its root: the static
        /// mesh on the kit's signal atlas, the lenses on the shared lamp
        /// material, the halos (lit after dark, by the clock), and a box up
        /// every signal pole on a Solid-layer object named
        /// <see cref="CitySigns.PostName"/> (the audits know the name: a pole
        /// beside a road is neither a barrier nor in a lane). None casts into
        /// the sun map. Returns the meshes, for the tile to free.
        /// </summary>
        public static List<Mesh> Attach(GameObject root, SignalTile st)
        {
            var meshes = new List<Mesh>(3);
            if (st == null) return meshes;
            void Draw(string name, Mesh m, Material mat, bool on)
            {
                var g = new GameObject(name);
                g.transform.SetParent(root.transform, false);
                g.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = g.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.enabled = mat != null && on;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                SunShadows.Exclude(g);
                meshes.Add(m);
                if (name == "SignalHalos" && mat != null)
                {
                    var h = new HaloSet { mesh = m, r = mr, lens = st.haloLens, cols = new Color32[m.vertexCount] };
                    halos.Add(h);
                    if (NightGlow.On) Recolour(h);
                }
            }
            if (st.mesh != null) Draw("JunctionFurniture", st.mesh, StaticMaterial(), true);
            if (st.lamps != null) Draw("SignalLenses", st.lamps, LampMaterial(), true);
            if (st.halos != null) Draw("SignalHalos", st.halos, HaloMaterial(), NightGlow.On);
            if (st.poles.Count > 0)
            {
                var c = new GameObject(CitySigns.PostName) { layer = CityWorld.SolidLayer };
                c.transform.SetParent(root.transform, false);
                var origin = root.transform.position;
                foreach (var (foot, top) in st.poles)
                {
                    var bc = c.AddComponent<BoxCollider>();
                    bc.center = new Vector3(foot.x, (foot.y + top) * 0.5f, foot.z) - origin;
                    bc.size = new Vector3(PoleColliderW, top - foot.y, PoleColliderW);
                }
            }
            return meshes;
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
