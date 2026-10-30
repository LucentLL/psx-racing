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
    public static partial class CitySignals
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

        /// <summary>What a CHANNEL (a phase of a plan at an offset,
        /// CitySignals.Control) shows at clock time t (seconds). THE one
        /// clock: every head in the city and the traffic. Channels 0 and 1 are
        /// the old two-phase clock (a group number read as a channel).</summary>
        public static Aspect AspectOf(int channel, double t)
        {
            if (channel < 0 || channel >= chans.Count)
            {
                double w = t % CycleS;
                if (w < 0) w += CycleS;
                if (channel != 0) { w -= HalfCycleS; if (w < 0) w += CycleS; }
                return w < GreenS ? Aspect.Green : w < GreenS + YellowS ? Aspect.Yellow : Aspect.Red;
            }
            var c = chans[channel];
            double u = (t + c.offset - c.start) % c.cycle;
            if (u < 0) u += c.cycle;
            if (u < c.green) return Aspect.Green;
            if (u < c.green + c.yellow) return Aspect.Yellow;
            return Aspect.Red;
        }

        /// <summary>What an approach's head shows now.</summary>
        public static Aspect AspectNow(Approach a) => AspectOf(a.channel, Now);

        /// <summary>The clock's last time (<see cref="Tick"/>).</summary>
        public static double Now { get; private set; }

        static byte[] lastAspect = new byte[0];
        static bool[] changedNow = new bool[0];
        static bool lastNight, lampsDirty = true;

        /// <summary>
        /// Advance the one clock (CityWorld.Update; a tool calls it once after
        /// standing its tiles up). Rewrites the lens texture and the halos'
        /// colours only when a phase changes or night falls.
        /// </summary>
        public static void Tick(double t)
        {
            Now = t;
            int n = Mathf.Max(2, chans.Count);
            if (lastAspect.Length != n)
            {
                lastAspect = new byte[n]; changedNow = new bool[n];
                for (int i = 0; i < n; i++) lastAspect[i] = 255;
                lampsDirty = true;
            }
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                byte s = (byte)AspectOf(i, t);
                if (s == lastAspect[i]) continue;
                lastAspect[i] = s; changedNow[i] = true; any = true;
            }
            bool night = NightGlow.On;
            bool nightFlip = night != lastNight;
            if (!any && !nightFlip && !lampsDirty) return;
            lastNight = night;
            WriteLamps();
            lampsDirty = false;
            // the halos: only the tiles that hold a channel that changed
            for (int i = halos.Count - 1; i >= 0; i--)
            {
                var h = halos[i];
                if (h.r == null || h.mesh == null) { halos.RemoveAt(i); continue; }
                h.r.enabled = night;
                if (!night) continue;
                bool touch = nightFlip || h.chans == null;
                if (!touch) foreach (int c in h.chans) if (c < n && changedNow[c]) { touch = true; break; }
                if (touch) Recolour(h);
            }
            if (any) System.Array.Clear(changedNow, 0, n);
        }

        // lens colours, lit and dark (sRGB). INCANDESCENT (the 1990s city,
        // 2026-10-29, the owner: "LEDs were not common"): a 1999 signal is a
        // filament behind a tinted lens - its red leans orange, its amber is a
        // yellower gold and its blue-green carries the filament's white; the
        // LED's saturated red, amber and mint (255,36,24 / 255,176,20 / 40,255,150)
        // were today's. The halo is softer and wider (HaloSizeM, _Strength).
        static readonly Color32[] LitColour = { new Color32(255, 62, 30, 255), new Color32(255, 186, 46, 255), new Color32(96, 236, 168, 255) };
        static readonly Color32[] DarkColour = { new Color32(46, 10, 8, 255), new Color32(44, 30, 6, 255), new Color32(6, 36, 24, 255) };
        static readonly Color32 Black = new Color32(0, 0, 0, 255);

        static bool Lit(int channel, int colour) =>
            (channel < lastAspect.Length && lastAspect[channel] != 255 ? lastAspect[channel] : (int)AspectOf(channel, Now)) == colour;

        static Texture2D lampTex;
        static Material lampMat, haloMat;
        static bool warned;

        static Color32[] lampPx;

        /// <summary>The lens texture's height: a row per channel, a power of two.</summary>
        static int LampRows => Mathf.NextPowerOfTwo(Mathf.Max(2, chans.Count));

        static void WriteLamps()
        {
            if (lampTex == null) return;
            int rows = lampTex.height, used = Mathf.Max(2, chans.Count);
            if (lampPx == null || lampPx.Length != rows * 4) lampPx = new Color32[rows * 4];
            for (int g = 0; g < rows; g++)
            {
                for (int c = 0; c < 3; c++) lampPx[g * 4 + c] = g < used && Lit(g, c) ? LitColour[c] : DarkColour[c];
                lampPx[g * 4 + 3] = Black;
            }
            lampTex.SetPixels32(lampPx);
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
        /// emitter the halation glows by), on a 4 x N texture - a column per
        /// lens colour, a row per CHANNEL (a plan's phase at an offset) - that
        /// <see cref="Tick"/> rewrites when a channel changes. Every lens in
        /// the city samples it.
        /// </summary>
        public static Material LampMaterial()
        {
            if (lampMat != null && lampTex != null && lampTex.height == LampRows) return lampMat;
            if (lampMat != null && lampTex != null)
            {
                // the plans changed (another map): the texture at the new height
                lampTex.Reinitialize(4, LampRows);
                lampPx = null;
                WriteLamps();
                return lampMat;
            }
            var baseMat = StaticMaterial();
            if (baseMat == null)
            {
                if (!warned) { warned = true; Debug.LogWarning("[City] the city kit has no signals material - the signal heads are not drawn (run a city build: PSXRacingBuilder.EnsureCityKit)."); }
                return null;
            }
            lampTex = new Texture2D(4, LampRows, TextureFormat.RGBA32, false)
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
            lampsDirty = true;
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
            haloMat.SetFloat("_Strength", 1.05f);
            return haloMat;
        }

        /// <summary>A lens halo's half size (a street lamp's is 2.6 m): an
        /// incandescent lens's glow, wider and softer than the LED's 1.1.</summary>
        public const float HaloSizeM = 1.4f;

        sealed class HaloSet { public Mesh mesh; public Renderer r; public ushort[] lens; public int[] chans; public Color32[] cols; }
        static readonly List<HaloSet> halos = new List<HaloSet>();

        static void Recolour(HaloSet h)
        {
            for (int i = 0; i < h.lens.Length; i++)
            {
                int g = h.lens[i] >> 2, c = h.lens[i] & 3;   // the channel, the colour
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

        // ---- crosswalks (roads pass L7, owner Q5 a) -------------------------
        /// <summary>A crosswalk starts this far out from the junction's patch,
        /// is this wide (10 ft), and its stop bar stands StopBackM (4 ft,
        /// MUTCD 3B.16) behind it: the bar's centre this far from the patch.</summary>
        public const float CrossInsetM = 0.3f, CrossWideM = 3.0f;
        public const float CrossStopBackM = CrossInsetM + CrossWideM + StopBackM + 0.5f * BarM;
        public static bool CrosswalksOn = System.Environment.GetEnvironmentVariable("PSX_CITY_CROSSWALKS") != "0";

        /// <summary>One crosswalk: the arm and the node it meets the junction
        /// at; the arc positions of its near and far edges and of the stop bar.</summary>
        public struct Crosswalk { public int edge, node, junction; public float sNear, sFar, sStop; }
        static List<Crosswalk> crosswalks;
        static Dictionary<long, int> crosswalkAt;
        public static int CrosswalkJunctions, CrosswalkArmsSkipped;

        /// <summary>Every crosswalk (tier-1 signalised junctions' arms).</summary>
        public static IReadOnlyList<Crosswalk> Crosswalks(CityMap map, CityMeshes.Trims trims) { Ensure(map, trims); return crosswalks; }

        /// <summary>The stop bar's distance from the patch on an arm with a
        /// crosswalk (0: none).</summary>
        /// <summary>Roads pass L8 (plan A13): how node n is controlled - 4 a
        /// signalised junction's member, 3 an all-way stop, 2 a stop on some
        /// approach, 0 nothing - and whether the approach of
        /// <paramref name="edgeA"/> or <paramref name="edgeB"/> into it stops.</summary>
        public static int ControlAt(CityMap map, CityMeshes.Trims trims, int n, int edgeA, int edgeB, out bool throughStops)
        {
            throughStops = false;
            Ensure(map, trims);
            if (junctions == null) return 0;
            if (nodeJunction == null)
            {
                nodeJunction = new Dictionary<int, Junction>(junctions.Count * 2);
                foreach (var jj in junctions) foreach (int m in jj.nodes) nodeJunction[m] = jj;
            }
            if (!nodeJunction.TryGetValue(n, out var j)) return 0;
            if (j.warrant) return 0;                                  // the paint answers to OSM's controls
            if (j.signal) return 4;
            if (j.allWay) return 3;
            bool any = false;
            foreach (var a in j.approaches)
            {
                if (a.ctl != 2) continue;
                any = true;
                if (a.node == n && (a.edge == edgeA || a.edge == edgeB)) throughStops = true;
            }
            return any ? 2 : 0;
        }
        static Dictionary<int, Junction> nodeJunction;

        public static float CrosswalkStopBack(CityMap map, CityMeshes.Trims trims, CityMap.Edge e, int node)
        {
            Ensure(map, trims);
            return crosswalkAt != null && crosswalkAt.ContainsKey(((long)e.index << 1) | (e.b == node ? 1L : 0L)) ? CrossStopBackM : 0f;
        }

        /// <summary>Does this arm take a crosswalk: a street (not a freeway
        /// or a ramp, not in a tunnel), its own end at the junction (not
        /// clipped, not inside a cluster), with ribbon for the crosswalk and
        /// its stop bar?</summary>
        static bool CrosswalkFits(CityMap map, CityMeshes.Trims trims, CityMap.Edge e, int n)
        {
            if (e.a == e.b || e.tunnel || e.link || e.cls >= 5 || e.length < 6f) return false;
            if (trims.Internal(e.index) || trims.BranchAt(e, n) >= 0) return false;
            // only where the arm meets a junction's patch (a road mitred on
            // through a signal cluster's node has no mouth there)
            if (!trims.patch[n] || trims.TrimAt(e, n) < 0.5f) return false;
            float rib = e.length - trims.atA[e.index] - trims.atB[e.index];
            return rib >= CrossStopBackM + 2f;
        }

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
            /// <summary>Signals: the junction's phase (0 = the main road's) and
            /// the clock's channel its heads show.</summary>
            public byte group;
            public ushort channel;
            /// <summary>STOP and yield junctions: 2 STOP, 1 YIELD (a give_way
            /// tag: no sign of ours, no bar - the road paint's yield line).</summary>
            public byte ctl;
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
            /// <summary>Decided by the MUTCD warrants where OSM says nothing
            /// (CitySignals.Control); the road paint ignores it.</summary>
            public bool warrant;
            /// <summary>Crosswalks on its arms (the plan walks them).</summary>
            public bool walks;
            /// <summary>Signals: the plan, its phases, the controller it runs
            /// in (its own id, or the junction it is linked to).</summary>
            public int plan = -1, controller = -1;
            public byte phases;
            /// <summary>Signals: no right turn on red (uptown's walked
            /// junctions, where the city posts NO TURN ON RED).</summary>
            public bool NoTurnOnRed => signal && uptown && walks;
            /// <summary>Only yields (give_way tags): no STOP here.</summary>
            public bool YieldOnly { get { foreach (var a in approaches) if (a.ctl != 1) return false; return !signal && approaches.Count > 0; } }
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
            nodeJunction = null;
            junctions = new List<Junction>();
            byTile = new Dictionary<long, List<Junction>>();
            crosswalks = new List<Crosswalk>();
            crosswalkAt = new Dictionary<long, int>();
            CrosswalkJunctions = 0; CrosswalkArmsSkipped = 0;
            Counts = default;
            ResetPlans();
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
            foreach (int root in keys) if (BuildCluster(clusters[root], root, false)) Counts.osmSignals++;

            // one signalised junction per cluster: its approaches, its crosswalks
            // (OSM's only: a warrant's leaves the road paint as it was)
            bool BuildCluster(List<int> ns, int root, bool warrant)
            {
                var j = new Junction { signal = true, nodes = ns.ToArray(), warrant = warrant };
                int arms = 0;
                // roads pass L7 (Q5 a): a tier-1 signalised junction (its best
                // arm tier 1) has a crosswalk on every arm that takes one
                int tier = 3;
                foreach (int n in ns)
                    foreach (int ei in map.nodeEdges[n])
                    {
                        var e = map.edges[ei];
                        if (e.a == e.b || inCluster[e.a == n ? e.b : e.a] == root) continue;
                        tier = Mathf.Min(tier, CityTier.Of(e));
                    }
                bool walks = !warrant && CrosswalksOn && tier == CityTier.T1;
                cwPending.Clear();
                int skipped = 0;
                foreach (int n in ns)
                    foreach (int ei in map.nodeEdges[n])
                    {
                        var e = map.edges[ei];
                        if (e.a == e.b) continue;
                        int o = e.a == n ? e.b : e.a;
                        if (inCluster[o] == root) continue;
                        arms++;
                        bool cw = walks && CrosswalkFits(map, trims, e, n);
                        if (walks && !cw && !e.link && e.cls < 5) skipped++;
                        if (cw)
                        {
                            bool atA = e.a == n;
                            float mouth = atA ? trims.atA[e.index] : e.length - trims.atB[e.index], dir = atA ? 1f : -1f;
                            cwPending.Add(new Crosswalk { edge = e.index, node = n, sNear = mouth + dir * CrossInsetM,
                                                         sFar = mouth + dir * (CrossInsetM + CrossWideM), sStop = mouth + dir * CrossStopBackM });
                        }
                        if (MakeApproach(map, trims, e, n, out var a, cw ? CrossStopBackM : StopBackM)) j.approaches.Add(a);
                    }
                if (arms < 3 || j.approaches.Count < 2) return false;
                for (int i = 0; i < j.approaches.Count; i++) { var ap = j.approaches[i]; ap.ctl = 4; j.approaches[i] = ap; }
                Finish(map, j, root);
                j.walks = cwPending.Count > 0;
                if (cwPending.Count > 0) CrosswalkJunctions++;
                CrosswalkArmsSkipped += skipped;
                foreach (var c in cwPending)
                {
                    var cc = c; cc.junction = j.id;
                    var ce = map.edges[c.edge];
                    crosswalkAt[((long)c.edge << 1) | (ce.b == c.node ? 1L : 0L)] = crosswalks.Count;
                    crosswalks.Add(cc);
                }
                return true;
            }

            // ---- stops
            var flags = LoadStopFlags();
            var stopAt = new Dictionary<int, Junction>();
            var seen = new HashSet<long>();
            void AddStop(int n, CityMap.Edge e, bool allWay, byte ctl = 2, bool warrant = false)
            {
                if (inCluster[n] >= 0 || sig[n]) return;              // a signal wins
                if (!seen.Add(((long)e.index << 1) | (e.b == n ? 1L : 0L))) return;
                if (!MakeApproach(map, trims, e, n, out var a)) return;
                a.ctl = ctl;
                if (!stopAt.TryGetValue(n, out var j)) stopAt[n] = j = new Junction { signal = false, nodes = new[] { n }, warrant = warrant };
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
            // ---- yields (highway=give_way): the tagged way's approach that is
            // not the node's through road, or the approach it stands on
            foreach (var t in map.tagged)
            {
                if (t.kind != 1 || t.edge < 0 || t.edge >= map.edges.Length) continue;
                var e = map.edges[t.edge];
                float d0 = t.s, d1 = e.length - t.s;
                int n = d0 <= d1 ? e.a : e.b;
                if (Degree(map, n) < 3) continue;
                if (Mathf.Min(d0, d1) < 1.5f)
                {
                    ThroughArms(map, n, out int t1, out int t2);
                    foreach (int ei in map.nodeEdges[n])
                    {
                        var o = map.edges[ei];
                        if (o.wayId != t.wayId || ei == t1 || ei == t2) continue;
                        if (o.oneway && o.a == n) continue;
                        AddStop(n, o, false, 1);
                    }
                }
                else if (Mathf.Min(d0, d1) <= StopSnapM)
                {
                    if (e.oneway && e.a == n) continue;
                    AddStop(n, e, false, 1);
                }
            }

            // ---- THE WARRANTS where OSM says nothing (CitySignals.Control)
            if (WarrantsOn)
            {
                var taken = new bool[nn];
                var sigMember = new bool[nn];
                for (int i = 0; i < nn; i++) { sigMember[i] = member[i] || sig[i]; taken[i] = sigMember[i]; }
                foreach (var kv in stopAt) taken[kv.Key] = true;
                var wsig = new bool[nn];
                foreach (var d in Warrants(map, trims, taken, sigMember))
                {
                    if (d.signal) { wsig[d.node] = true; Counts.warrantSignalNodes++; continue; }
                    foreach (int ei in d.stops) AddStop(d.node, map.edges[ei], d.allWay, 2, true);
                }
                // the warrants' signals: one junction per cluster, as OSM's
                for (int i = 0; i < nn; i++)
                {
                    if (!wsig[i]) continue;
                    foreach (int ei in map.nodeEdges[i])
                    {
                        var e = map.edges[ei];
                        if (e.a == e.b || e.length > ClusterM) continue;
                        int o = e.a == i ? e.b : e.a;
                        if (wsig[o]) Union(i, o);
                    }
                }
                var wcl = new Dictionary<int, List<int>>();
                for (int i = 0; i < nn; i++)
                {
                    if (!wsig[i]) continue;
                    int r = Find(i);
                    if (!wcl.TryGetValue(r, out var l)) wcl[r] = l = new List<int>(4);
                    l.Add(i);
                }
                var wkeys = new List<int>(wcl.Keys);
                wkeys.Sort();
                foreach (int r in wkeys)
                {
                    var ns = wcl[r];
                    ns.Sort();
                    int root = ns[0];
                    foreach (int n in ns) inCluster[n] = root;
                    if (BuildCluster(ns, root, true)) Counts.warrantSignals++;
                    else foreach (int n in ns) inCluster[n] = -1;
                }
            }

            var stopKeys = new List<int>(stopAt.Keys);
            stopKeys.Sort();
            foreach (int n in stopKeys)
            {
                var sj = stopAt[n];
                if (inCluster[n] >= 0) continue;                       // a warrant's signal took it
                Finish(map, sj, n);
                bool stopsHere = false;
                foreach (var a in sj.approaches) if (a.ctl == 2) stopsHere = true;
                if (!stopsHere) Counts.osmYieldJ++;
                else if (sj.warrant) { if (sj.allWay) Counts.warrantAllWay++; else Counts.warrantStopJ++; Counts.warrantStopApp += sj.approaches.Count; }
                else { Counts.osmStopJ++; if (sj.allWay) Counts.osmAllWay++; }
            }

            // ---- the signals' phases and plans, over every signal junction
            PlanSignals(map);

            int sigs = 0, sigApp = 0, stops = 0, stopApp = 0;
            foreach (var j in junctions) { if (j.signal) { sigs++; sigApp += j.approaches.Count; } else { stops++; stopApp += j.approaches.Count; } }
            Debug.Log($"[City] junction controls: {sigs} signalised junctions ({sigApp} approaches), {stops} stop/yield-controlled ({stopApp} approaches) from {map.tagged.Length} tagged nodes, {flags.Count} stop tags; " + ControlReport());
        }

        /// <summary>The approach an edge makes into node n, if it makes one.</summary>
        static readonly List<Crosswalk> cwPending = new List<Crosswalk>(8);

        static bool MakeApproach(CityMap map, CityMeshes.Trims trims, CityMap.Edge e, int n, out Approach a, float stopBack = StopBackM)
        {
            a = default;
            if (e.a == e.b || e.tunnel || e.roundabout) return false;
            if (trims.Internal(e.index)) return false;                // inside a junction cluster (roads pass L7)
            if (e.cls >= 5 && !e.link) return false;                  // no freeway mainline
            if (e.oneway && e.a == n) return false;                   // it leaves the junction
            if (e.length < 6f) return false;
            bool atB = e.b == n;
            float trim = trims.TrimAt(e, n);
            float sMouth = atB ? e.length - trim : trim;
            float sStop = atB ? sMouth - stopBack : sMouth + stopBack;
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
                // the middle between the two directions (roads pass L2: the line
                // set's double yellow or TWLTL centre, which on an uneven split
                // is not the lanes' centre; + = left of a->b), in travel-right
                // metres, and the lanes that come IN: nF arriving at b, nB at a
                float divider = atB ? -LineModel.DividerLat(e) : LineModel.DividerLat(e);
                bool twltl = e.hasLset ? e.lsCentre == 2 : e.turnLane;
                inL = divider + (twltl ? 1.7f : 0.15f);
                lanesIn = e.hasLset ? (atB ? e.lsNF : e.lsNB) : Mathf.Max(1, (e.lanes - (e.turnLane ? 1 : 0)) / 2);
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
            // a signal's phases, plan and channels: PlanSignals, once every
            // junction stands (two may run as one controller)
            junctions.Add(j);
            long k = Key(Mathf.FloorToInt(j.centre.x / CityMeshes.TileSize), Mathf.FloorToInt(j.centre.y / CityMeshes.TileSize));
            if (!byTile.TryGetValue(k, out var l)) byTile[k] = l = new List<Junction>(4);
            l.Add(j);
        }

        // ==================================================================
        //  A TILE
        // ==================================================================

        public struct Head { public Vector3 pos; public Vector2 face; public byte group; public ushort channel; }
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
            /// <summary>Round 3: the heads every signalised approach of the tile's
            /// junctions shows, and how they are carried (the head census).</summary>
            public readonly List<ApproachHeads> approachHeads = new List<ApproachHeads>();
            /// <summary>Round 3's fallbacks: far-left mast arms, heads on a pole's
            /// side, post-mounted heads.</summary>
            public int leftArms, poleHeads, postHeads, backSpans;
            /// <summary>The halos' lens codes (channel << 2 | colour), in mesh order.</summary>
            public ushort[] haloLens;
            public float ms;
        }

        /// <summary>The heads facing one signalised approach (junction id, its
        /// index in Junction.approaches) and what carries them (How* bits).</summary>
        public struct ApproachHeads { public int junction, approach, heads, view; public byte how; }
        public const byte HowSpan = 1, HowMast = 2, HowMastLeft = 4, HowPole = 8, HowPostFar = 16, HowPostLeft = 32, HowPostNear = 64;

        /// <summary>
        /// ROAD LOGIC JUNCTIONS, ROUND 3 - EVERY SIGNALISED APPROACH SHOWS ITS
        /// HEADS (PSX_CITY_HEADS=0: the before). The span wire and the far-right
        /// mast arm could both be refused - every foot on the occupancy mask, the
        /// arm out of reach - and nothing else was tried, so a signal ran with no
        /// head in its drivers' view (Colony Road at Carnegie Boulevard). Now:
        /// one lane's approach hangs TWO heads 2.4 m apart (MUTCD: two signal
        /// faces at least for the through movement, 8 ft apart), and an approach
        /// short of two after the wire or its arm gets, in this order, a mast
        /// arm from its FAR-LEFT corner (the one corner free), a head on the
        /// side of the pole already standing at its far-right corner, a
        /// post-mounted head (a 1990s pedestal: 4.5 in post, three 12 in
        /// incandescent sections, the housing's bottom 2.9 m up) at the far-right
        /// corner, the far-left, then beside its stop line. Every new foot passes
        /// the same test as the poles' (off every lane and fan, no building, lot,
        /// driveway, water, deck, lamp or sign, no race run-off) and keeps off
        /// the crosswalks' curb ramps.
        /// </summary>
        public static bool HeadsEvery = System.Environment.GetEnvironmentVariable("PSX_CITY_HEADS") != "0";

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
            // the stop bars: every signal approach and every STOP (a yield
            // has the road paint's yield line)
            foreach (var a in j.approaches) { if (!j.signal && a.ctl != 2) continue; StopBar(a); st.stopBars++; }
            if (!j.signal)
            {
                foreach (var a in j.approaches)
                {
                    if (a.ctl != 2) continue;
                    if (PlaceStop(a, out var s)) { st.stops.Add(s); EmitStopSign(s, a); }
                    else st.refused++;
                }
                return;
            }
            st.signalJunctions++;
            int n = j.approaches.Count;
            furnishJ = j;
            if (furnishHeads.Length < n) { furnishHeads = new int[n]; furnishView = new int[n]; furnishHow = new byte[n]; }
            for (int i = 0; i < n; i++) { furnishHeads[i] = 0; furnishView[i] = 0; furnishHow[i] = 0; }
            jPoles.Clear(); poleUsed.Clear();
            try
            {
                // each approach's far-right corner
                var corner = new Vector2?[n];
                for (int i = 0; i < n; i++)
                    if (FarCorner(j, j.approaches[i], out var f)) corner[i] = f;
                // outside uptown: a span wire between an opposing pair's corners
                howNow = HowSpan;
                if (!j.uptown && SpanWire(j, corner, st)) st.spanWires++;
                else
                {
                    howNow = HowMast;
                    for (int i = 0; i < n; i++)
                    {
                        if (corner[i] == null) { st.refused++; continue; }
                        if (MastArm(j, j.approaches[i], corner[i].Value, st, 1)) st.mastArms++;
                        else st.refused++;
                    }
                }
                // round 3: two faces at least for every approach's through movement
                if (HeadsEvery)
                {
                    // suburban and arterial (span wires): the set-back wire first;
                    // uptown (mast arms): the arms and posts first, the wire last
                    if (!j.uptown) BackSpan(j, st);
                    for (int i = 0; i < n; i++)
                        if (furnishView[i] < 2) Supplement(j, i, corner[i], st);
                    if (j.uptown) BackSpan(j, st);
                }
            }
            finally
            {
                for (int i = 0; i < n; i++)
                    st.approachHeads.Add(new ApproachHeads { junction = j.id, approach = i, heads = furnishHeads[i], view = furnishView[i], how = furnishHow[i] });
                furnishJ = null;
            }
        }

        // ---- round 3: the head census and the fallbacks ---------------------

        static Junction furnishJ;
        static int[] furnishHeads = new int[0], furnishView = new int[0];
        static byte[] furnishHow = new byte[0];
        /// <summary>The junction's mast-arm and span poles (a head can go on
        /// their side), and which approach already has one on which pole.</summary>
        static readonly List<Vector2> jPoles = new List<Vector2>(8);
        static readonly HashSet<int> poleUsed = new HashSet<int>();
        static byte howNow;

        /// <summary>IN THE DRIVER'S VIEW (MUTCD 4D.14: a primary face 40 to 180 ft beyond
        /// the stop line, a near-side face a supplement): from 6 m short of the stop
        /// line to 55 m past it, within 20 m either side of the lanes' middle. A
        /// head the first clear corner put 60 m on down the road (Colony Road at
        /// Carnegie Boulevard) is not one the driver reads.</summary>
        public const float ViewNearM = -6f, ViewFarM = 55f, ViewLatM = 20f;

        public static bool InView(Approach a, Vector2 p)
        {
            var d = p - a.at;
            float along = Vector2.Dot(d, a.u), lat = Vector2.Dot(d, a.Right) - (a.inL + a.inR) * 0.5f;
            return along >= ViewNearM && along <= ViewFarM && Mathf.Abs(lat) <= ViewLatM;
        }

        /// <summary>The housing's bottom over the ground of a post- or
        /// pole-mounted head (MUTCD: 8 to 15 ft over the sidewalk), the
        /// pedestal's post (4.5 in) and its cast base.</summary>
        const float PostHeadBottomM = 2.9f, SigPostW = 0.115f, SigBaseW = 0.34f, SigBaseH = 0.42f;

        /// <summary>Approaches with NO head in view: a span wire between corners SET
        /// BACK past whatever keeps the near ones clear (a city race route's 8 m
        /// run-off either side - Tryon Street uptown and north - lots, lamps), its
        /// heads over those approaches only, in their view.</summary>
        static void BackSpan(Junction j, SignalTile st)
        {
            int n = j.approaches.Count;
            bool[] want = null;
            for (int i = 0; i < n; i++) if (furnishView[i] == 0) { if (want == null) want = new bool[n]; want[i] = true; }
            if (want == null) return;
            var back = new Vector2?[n];
            for (int i = 0; i < n; i++)
                if (SideCorner(j, j.approaches[i], 1, 1.0f, PoleW * 0.5f, 3f, out var fb, BackSpanM)) back[i] = fb;
            howNow = HowSpan;
            if (SpanWire(j, back, st, want)) { st.spanWires++; st.backSpans++; }
        }

        /// <summary>An approach short of two faces after its wire or arm: a far-left
        /// mast arm (none at all), a head on its far-right pole, post-mounted heads
        /// at the far-right corner, the far-left, beside the stop line.</summary>
        static void Supplement(Junction j, int i, Vector2? farRight, SignalTile st)
        {
            var a = j.approaches[i];
            if (furnishView[i] == 0 && SideCorner(j, a, -1, 1.0f, PoleW * 0.5f, 3f, out var fl))
            {
                howNow = HowMastLeft;
                if (MastArm(j, a, fl, st, -1)) { st.mastArms++; st.leftArms++; }
            }
            // a head on the side of a pole of this junction's own on the far side
            // (its far-right, the cross street's that stands at its far left)
            for (int k = 0; k < 2 && furnishView[i] < 2; k++)
            {
                if (!JunctionPole(a, i, true, out int pi)) break;
                howNow = HowPole;
                PoleHead(a, jPoles[pi], st);
                poleUsed.Add(pi * 64 + i);
                st.poleHeads++;
            }
            for (int pass = 0; pass < 3 && furnishView[i] < 2; pass++)
            {
                Vector2 f;
                bool ok;
                if (pass == 0)
                {
                    howNow = HowPostFar;
                    ok = farRight != null && InView(a, farRight.Value) && PostFootOk(j, a, farRight.Value, FarPostOffM);
                    f = ok ? farRight.Value : default;
                    if (!ok) ok = SideCorner(j, a, 1, FarPostOffM, SigBaseW * 0.5f, 2.5f, out f);
                }
                else if (pass == 1) { howNow = HowPostLeft; ok = SideCorner(j, a, -1, FarPostOffM, SigBaseW * 0.5f, 2.5f, out f); }
                else { howNow = HowPostNear; ok = NearRight(j, a, out f); }
                if (!ok) continue;
                PostHead(a, f, st);
                st.postHeads++;
            }
            // last: a near-side head on a pole of the junction's beside the approach
            if (furnishView[i] < 2 && JunctionPole(a, i, false, out int pn))
            {
                howNow = HowPole;
                PoleHead(a, jPoles[pn], st);
                poleUsed.Add(pn * 64 + i);
                st.poleHeads++;
            }
        }

        /// <summary>The junction's pole best placed to carry a head for approach
        /// a (index i): on its far side in the driver's view (<paramref name="far"/>),
        /// else beside it short of the junction; none it already uses.</summary>
        static bool JunctionPole(Approach a, int i, bool far, out int best)
        {
            best = -1;
            float bestScore = float.MaxValue, mid = (a.inL + a.inR) * 0.5f;
            for (int k = 0; k < jPoles.Count; k++)
            {
                if (poleUsed.Contains(k * 64 + i)) continue;
                var d = jPoles[k] - a.at;
                float along = Vector2.Dot(d, a.u), lat = Vector2.Dot(d, a.Right) - mid;
                if (Mathf.Abs(lat) > ViewLatM) continue;
                if (far ? (along < 4f || along > ViewFarM) : (along < ViewNearM || along >= 4f)) continue;
                float score = Mathf.Abs(lat) + Mathf.Abs(along - 20f) * 0.3f;
                if (score < bestScore) { bestScore = score; best = k; }
            }
            return best >= 0;
        }

        /// <summary>A post-mounted head's foot: the poles' test, its own ground
        /// near the road's, off the crosswalks' curb ramps.</summary>
        static bool PostFootOk(Junction j, Approach a, Vector2 f, float offM = 0.6f)
            => FootOk(f, offM, SigBaseW * 0.5f) && Mathf.Abs(Ground(f) - a.roadY) <= 2.5f && !OnCurbRamp(j, f, SigBaseW * 0.5f);

        /// <summary>A far-corner post keeps the poles' 1 m off the pavement (a turn that
        /// clips the corner's kerb must not reach it); beside the stop line a STOP
        /// sign's 0.6 m (MUTCD: 2 ft behind the kerb face).</summary>
        const float FarPostOffM = 1.0f;

        /// <summary>A set-back span wire's poles: up to this far past the pavement
        /// (a race run-off's 8 m and a pole's room).</summary>
        const float BackSpanM = 14f;

        /// <summary>The approach's far corner on its right (<paramref name="side"/>
        /// 1, FarCorner's walk) or its LEFT (-1: along the line just outside the
        /// road's left pavement edge - beyond the oncoming lanes, or a divided
        /// road's median - on across the junction): the first clear spot.</summary>
        static bool SideCorner(Junction j, Approach a, int side, float offM, float r, float dyMax, out Vector2 foot, float maxOut = 4.8f)
        {
            foot = default;
            var rt = a.Right;
            var node = mapNow.nodes[a.node];
            float t0 = Vector2.Dot(node - a.at, a.u) + 2f;
            float edgeLat = side > 0 ? a.inR : LeftEdge(a);
            for (float extra = offM + r; extra <= maxOut; extra += 1.2f)
                for (float t = t0; t <= t0 + 40f && t <= ViewFarM; t += 1.5f)
                {
                    var f = a.at + rt * (edgeLat + side * (0.3f + extra)) + a.u * t;
                    if (!FootOk(f, offM, r)) continue;
                    if (Mathf.Abs(Ground(f) - a.roadY) > dyMax) continue;
                    if (OnCurbRamp(j, f, r)) continue;
                    foot = f;
                    return true;
                }
            return false;
        }

        /// <summary>The road's left pavement edge at the stop line, as an offset
        /// right of the approach's line (negative: its left).</summary>
        static float LeftEdge(Approach a)
        {
            var e = mapNow.edges[a.edge];
            bool towardB = e.b == a.node;
            // PaveEdgeM: side > 0 the extent right of a->b, else left of it
            float left = towardB ? e.PaveEdgeM(a.sStop, -1) : e.PaveEdgeM(a.sStop, 1);
            return Mathf.Min(a.inL, -left);
        }

        /// <summary>Beside the stop line on the right (a STOP sign's walk): a
        /// near-side post-mounted head.</summary>
        static bool NearRight(Junction j, Approach a, out Vector2 foot)
        {
            foot = default;
            var r = a.Right;
            foreach (float o in StopOut)
                foreach (float t in StopAlong)
                {
                    var f = a.at + r * (a.inR + 0.3f + o) + a.u * t;
                    if (!PostFootOk(j, a, f)) continue;
                    foot = f;
                    return true;
                }
            return false;
        }

        static Dictionary<int, List<Crosswalk>> cwByJunction;
        static List<Crosswalk> cwByJunctionOf;

        /// <summary>Is a foot of radius r on a curb ramp: the landing behind
        /// either end of one of the junction's crosswalks (its width and 0.6 m
        /// either side along the road, 2.5 m back from the pavement edge)?</summary>
        static bool OnCurbRamp(Junction j, Vector2 f, float r)
        {
            if (crosswalks == null || crosswalks.Count == 0) return false;
            if (cwByJunctionOf != crosswalks || cwByJunction == null)
            {
                cwByJunction = new Dictionary<int, List<Crosswalk>>();
                foreach (var cw in crosswalks)
                {
                    if (!cwByJunction.TryGetValue(cw.junction, out var l)) cwByJunction[cw.junction] = l = new List<Crosswalk>(4);
                    l.Add(cw);
                }
                cwByJunctionOf = crosswalks;
            }
            if (!cwByJunction.TryGetValue(j.id, out var mine)) return false;
            foreach (var cw in mine)
            {
                var e = mapNow.edges[cw.edge];
                float sMid = (cw.sNear + cw.sFar) * 0.5f, halfAlong = Mathf.Abs(cw.sFar - cw.sNear) * 0.5f + 0.6f;
                var c = e.PointAt(sMid);
                var t = e.TangentAt(sMid);
                var rgt = new Vector2(t.y, -t.x);
                Vector2 d = f - c;
                float along = Mathf.Abs(Vector2.Dot(d, t)), across = Vector2.Dot(d, rgt);
                if (along > halfAlong + r) continue;
                // right of a->b: the eMinus extent; left: ePlus
                float edge = across >= 0f ? e.PaveEdgeM(sMid, 1) : e.PaveEdgeM(sMid, -1);
                float out_ = Mathf.Abs(across) - edge;
                if (out_ > -r && out_ < 2.5f + r) return true;
            }
            return false;
        }

        /// <summary>A head on the side of the pole at <paramref name="pole"/>,
        /// facing the approach at a post-mounted head's height.</summary>
        static void PoleHead(Approach a, Vector2 pole, SignalTile st)
        {
            var face = new Vector3(-a.u.x, 0f, -a.u.y);
            var across = new Vector3(-face.z, 0f, face.x);
            float bottom = Mathf.Max(Ground(pole), a.roadY) + PostHeadBottomM;
            var p3 = new Vector3(pole.x, bottom + HeadH * 0.5f, pole.y);
            float off = PoleW * 0.5f + 0.1f + HeadD * 0.5f;
            // the two brackets from the pole to the housing's back
            for (int k = -1; k <= 1; k += 2)
                Box(p3 + Vector3.up * (k * HeadH * 0.3f) + face * (off * 0.5f), across * 0.03f, Vector3.up * 0.03f, face * (off * 0.5f), CellBlack, false);
            EmitHead(p3 + face * off, a, st);
        }

        /// <summary>A pedestal: a post-mounted three-section head at
        /// <paramref name="foot"/> facing the approach (its post and cast base on
        /// the atlas's metal, solid like the other poles).</summary>
        static void PostHead(Approach a, Vector2 foot, SignalTile st)
        {
            var face = new Vector3(-a.u.x, 0f, -a.u.y);
            var across = new Vector3(-face.z, 0f, face.x);
            float g = Ground(foot);
            float bottom = Mathf.Max(g, a.roadY) + PostHeadBottomM;
            float y0 = g - FootSinkM, y1 = bottom - 0.02f;
            Box(new Vector3(foot.x, (y0 + y1) * 0.5f, foot.y), across * (SigPostW * 0.5f), Vector3.up * ((y1 - y0) * 0.5f), face * (SigPostW * 0.5f), CellMetal, true);
            float b1 = g + SigBaseH;
            Box(new Vector3(foot.x, (y0 + b1) * 0.5f, foot.y), across * (SigBaseW * 0.5f), Vector3.up * ((b1 - y0) * 0.5f), face * (SigBaseW * 0.5f), CellMetal, true);
            // the slip fitter under the housing
            Box(new Vector3(foot.x, bottom - 0.05f, foot.y), across * 0.06f, Vector3.up * 0.05f, face * 0.06f, CellBlack, false);
            st.poles.Add((new Vector3(foot.x, y0, foot.y), y1));
            Claim(foot, SigBaseW * 0.5f);
            EmitHead(new Vector3(foot.x, bottom + HeadH * 0.5f, foot.y), a, st);
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
            // ... and past the junction's own pavement: the fan, its curb
            // returns, a cluster's ring (2026-10-04, West Trade Street: a span
            // wire's pole stood out on the fan, in the travelled way)
            if (CityMeshes.FurnitureOffFans && CityMeshes.JunctionPavementDistance(map, trimsNow, f, offM + r) < offM + r) return false;
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
                for (float t = t0; t <= t0 + 40f && (!HeadsEvery || t <= ViewFarM); t += 1.5f)
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
            // round 3: one lane's approach shows TWO faces, 2.4 m (8 ft) apart
            // either side of its middle (MUTCD: two for the through movement)
            if (HeadsEvery && n <= 1)
            {
                float m = (a.inL + a.inR) * 0.5f;
                into.Add(m - 1.2f); into.Add(m + 1.2f);
                return;
            }
            if (n <= 3) for (int k = 0; k < n; k++) into.Add(a.inL + w * (k + 0.5f));
            else { into.Add(a.inL + w * 0.5f); into.Add(a.inL + w * (n * 0.5f)); into.Add(a.inR - w * 0.5f); }
        }
        static readonly List<float> laneScratch = new List<float>(4);

        /// <summary>A mast arm from <paramref name="foot"/> out over the inbound
        /// lanes: from the right of them (<paramref name="side"/> 1, the far-right
        /// corner) or their left (-1, round 3's far-left corner).</summary>
        static bool MastArm(Junction j, Approach a, Vector2 foot, SignalTile st, int side)
        {
            var r = a.Right * side;
            float latF = Vector2.Dot(foot - a.at, r);
            LaneOffsets(a, laneScratch);
            float armY = j.roadY + HeadClearM + HeadH + 0.2f;
            float g = Ground(foot);
            var heads = new List<Vector2>(3);
            float reach = 0f;
            foreach (float o0 in laneScratch)
            {
                float o = o0 * side;
                float d = latF - o;
                if (d < 0.8f || d > MaxArmM - 0.3f) continue;
                heads.Add(foot - r * d);
                reach = Mathf.Max(reach, d);
            }
            if (heads.Count == 0) return false;
            Claim(foot, PoleW);
            if (furnishJ != null) jPoles.Add(foot);
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
        static bool SpanWire(Junction j, Vector2?[] corner, SignalTile st, bool[] want = null)
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
                    // every approach must hang at least one head from it (round 3's
                    // set-back wire: the wanted ones only, in their view, one at least)
                    var hangs = new List<(Vector2 p, float t, int app)>();
                    bool all = true;
                    int wanted = 0;
                    for (int q = 0; q < n && all; q++)
                    {
                        var a = j.approaches[q];
                        if (want != null && !want[q]) continue;
                        LaneOffsets(a, laneScratch);
                        int got = 0;
                        foreach (float o in laneScratch)
                        {
                            var p0 = a.at + a.Right * o;
                            if (!Cross(p0, a.u, A, B, out float along, out float t)) continue;
                            if (along < 1f || t < 0.08f || t > 0.92f) continue;
                            if (want != null && !InView(a, Vector2.Lerp(A, B, t))) continue;
                            hangs.Add((Vector2.Lerp(A, B, t), t, q));
                            got++;
                        }
                        if (got == 0 && want == null) all = false;
                        if (got > 0) wanted++;
                    }
                    if (!all || wanted == 0) continue;

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
                    if (furnishJ != null) { jPoles.Add(A); jPoles.Add(B); }
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
            if (furnishJ != null)
                for (int q = 0; q < furnishJ.approaches.Count; q++)
                    if (furnishJ.approaches[q].edge == a.edge && furnishJ.approaches[q].node == a.node)
                    {
                        furnishHeads[q]++; furnishHow[q] |= howNow;
                        if (InView(a, new Vector2(c.x, c.z))) furnishView[q]++;
                        break;
                    }
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
                LensQuad(lc, face, across, a.channel, k);
                haloCentres.Add(lc + face * 0.08f);
                haloLens.Add((ushort)(a.channel << 2 | LensCode(k)));
                haloYaw.Add(Mathf.Atan2(face.x, face.z));
            }
            st.heads.Add(new Head { pos = c, face = new Vector2(face.x, face.z), group = a.group, channel = a.channel });
        }

        /// <summary>The texture column of lens k (0 top = red, 1 yellow, 2 green).</summary>
        static int LensCode(int k) => k == 0 ? (int)Aspect.Red : k == 1 ? (int)Aspect.Yellow : (int)Aspect.Green;

        static void LensQuad(Vector3 c, Vector3 n, Vector3 across, int channel, int k)
        {
            var uv = new Vector2((LensCode(k) + 0.5f) / 4f, (channel + 0.5f) / LampRows);
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
        static readonly List<ushort> haloLens = new List<ushort>(256);
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

        static Mesh EndHalos(Vector3 origin, out ushort[] codes)
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
                    var hc = new HashSet<int>();
                    if (st.haloLens != null) foreach (var code in st.haloLens) hc.Add(code >> 2);
                    var h = new HaloSet { mesh = m, r = mr, lens = st.haloLens, chans = new List<int>(hc).ToArray(), cols = new Color32[m.vertexCount] };
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
