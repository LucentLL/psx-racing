using System.Collections.Generic;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    public static partial class CityAudit
    {
        // ------------------------------------------------------------------
        //  SIGN AUDIT (plan WP-23). The billboards, business pole signs and
        //  exit gantries are placed from the tile's RoadsideOccupancy mask;
        //  this checks what was placed against the geometry, measured again:
        //    * no post or leg on pavement, in a clear zone or under a deck,
        //      in a building or water, in race run-off, or on a cell the mask
        //      reserves (sight triangles, corner spots, lots, lamp feet);
        //    * nothing over pavement but a gantry's panels, and those at
        //      least 5.5 m over the highest road under them;
        //    * every face turned to its traffic (dot 0.9 or better to a driver
        //      150 m up the road, who is on a carriageway coming toward it);
        //    * every sign in the tile that placed it, none twice across a
        //      seam, the same signs on a second build;
        //    * the billboards per km of every route against NCDOT's figure,
        //      per class within +-20%, over every tile the routes run through;
        //    * one draw a tile; the billboards lit; posts solid, cabinets
        //      breakaway (Q15).
        // ------------------------------------------------------------------

        const float SignDensityTol = 0.2f;
        const float FaceDotMin = 0.9f;
        const float GantryClearMin = 5.5f;

        /// <summary>The sign audit alone: writes city_sign_audit.txt at the
        /// project root. Headless: -executeMethod PSXRacing.EditorTools.CityAudit.RunSigns</summary>
        [UnityEditor.MenuItem("PSX Racing/Audit City Signs")]
        public static void RunSigns()
        {
            outLog = new System.Text.StringBuilder();
            failures = 0;
            var map = CityMap.Get();
            if (map == null) { Fail("charlotte_city.bytes missing from Resources"); return; }
            SignAudit(map, CityMeshes.NodeTrims(map), CityBuildings.Precompute(map));
            outLog.AppendLine(failures == 0 ? "SIGN AUDIT OK" : $"SIGN AUDIT: {failures} FAILURES");
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "city_sign_audit.txt"), outLog.ToString());
        }

        static void SignAudit(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings)
        {
            Line("sign audit (WP-23): data " + (CitySignData.Loaded ? $"{CitySignData.Routes.Length} routes, {CitySignData.Boards.Length} OSM billboards, {CitySignData.Pois.Length} businesses" : "MISSING"));
            Check(CitySignData.Loaded, "the sign data (charlotte_signs.bytes) loads");
            PSXRacingBuilder.EnsureCityTextures();
            var kit = CityKit.Get();
            var sm = kit != null ? kit.signs : null;
            Check(sm != null && sm.mainTexture != null && sm.HasProperty("_NightFace") && sm.GetFloat("_NightFace") > 0.5f
                  && sm.GetFloat("_NightWin") > 0.5f && sm.GetTexture("_NightMask") != null,
                  "the city kit's sign material wears the atlas and lights its faces at night (_NightMask, _NightWin, _NightFace)");
            if (!CitySignData.Loaded) return;
            bool keepTrees = CityTrees.Enabled, keepOff = CitySigns.KeepOffPoles;
            CityTrees.Enabled = false;   // the signs only: the mask is the same, the trees come after them
            // PSX_SIGN_POLE_KEEP=0: the signs ignore the poles, to show the
            // wire check below has teeth (it must then fail)
            CitySigns.KeepOffPoles = System.Environment.GetEnvironmentVariable("PSX_SIGN_POLE_KEEP") != "0";
            if (!CitySigns.KeepOffPoles) Line("    PSX_SIGN_POLE_KEEP=0: the signs do NOT keep off the utility poles on this run");
            try { SignAuditInner(map, trims, buildings); }
            finally { CityTrees.Enabled = keepTrees; CitySigns.KeepOffPoles = keepOff; }
        }

        // ---- the WP-15 review: no utility wire, pole, crossarm or cobra-head
        //      through a sign, measured in 3D against what is drawn
        internal struct SignBox { public Vector3 c, x, y, z; public float hx, hy, hz; public string what; }

        /// <summary>The air a sign keeps from any wire or pole part, in 3D: a
        /// wire skimming a cabinet's top reads as through it from a driver's
        /// low eye (the review's frames); the placement keeps 0.5 m in plan.</summary>
        internal const float SignWireAirM = 0.3f;

        /// <summary>A box on horizontal axes: x across it (<paramref name="along"/>), y up, z along the normal.</summary>
        static SignBox BoxOn(Vector3 c, Vector3 along, float hx, float hy, float hz, string what)
        {
            along.y = 0f;
            var x = along.sqrMagnitude > 1e-8f ? along.normalized : Vector3.right;
            var z = Vector3.Cross(x, Vector3.up).normalized;
            return new SignBox { c = c, x = x, y = Vector3.up, z = z, hx = hx, hy = hy, hz = hz, what = what };
        }

        /// <summary>The boxes a tile's signs are drawn with, near enough: a
        /// business's cabinet and its posts; a billboard's faces, the catwalk
        /// and floodlights in front of each and the beam to it; a gantry's
        /// panels and truss; every solid post and leg.</summary>
        internal static void SignBoxesOf(CitySigns.SignTile st, List<SignBox> into)
        {
            into.Clear();
            foreach (var sg in st.signs) AddSignBoxes(st, sg, into);
            foreach (var (c, size, yaw) in st.posts)
            {
                float r = yaw * Mathf.Deg2Rad;
                into.Add(BoxOn(c, new Vector3(Mathf.Cos(r), 0f, -Mathf.Sin(r)), size.x * 0.5f, size.y * 0.5f, size.z * 0.5f, "a solid post or leg"));
            }
        }

        /// <summary>One sign's boxes (its solid posts and legs are the tile's: <see cref="SignBoxesOf"/>).</summary>
        internal static void AddSignBoxes(CitySigns.SignTile st, CitySigns.Sign sg, List<SignBox> into)
        {
            {
                if (sg.faces <= 0) return;
                var f0 = st.faces[sg.firstFace];
                var n0 = new Vector3(f0.normal.x, 0f, f0.normal.z);
                var across = Vector3.Cross(Vector3.up, n0);
                switch (sg.kind)
                {
                    case CitySigns.Kind.PoleSign:
                        into.Add(BoxOn(f0.centre, across, f0.w * 0.5f, f0.h * 0.5f, 0.22f, "a business cabinet"));
                        float gy = sg.ground - 0.3f;
                        into.Add(BoxOn(new Vector3(sg.pos.x, (gy + sg.bottom) * 0.5f, sg.pos.y), across, f0.w * 0.5f, (sg.bottom - gy) * 0.5f, 0.13f, "a business sign's posts"));
                        break;
                    case CitySigns.Kind.Bulletin:
                    case CitySigns.Kind.Poster:
                        for (int i = sg.firstFace; i < sg.firstFace + sg.faces; i++)
                        {
                            var fc = st.faces[i];
                            var n = new Vector3(fc.normal.x, 0f, fc.normal.z).normalized;
                            var ax = Vector3.Cross(Vector3.up, n);
                            float bottom = fc.centre.y - fc.h * 0.5f;
                            into.Add(BoxOn(fc.centre - n * 0.175f, ax, fc.w * 0.5f, fc.h * 0.5f, 0.175f, "a billboard face"));
                            var mid = new Vector3(fc.centre.x, bottom - 0.36f, fc.centre.z) + n * 0.685f;
                            into.Add(BoxOn(mid, ax, fc.w * 0.5f, 0.2f, 0.935f, "a billboard's catwalk and floodlights"));
                            var post = new Vector3(sg.pos.x, bottom - 0.35f, sg.pos.y);
                            var to = new Vector3(fc.centre.x, bottom - 0.35f, fc.centre.z) - post;
                            if (to.magnitude > 0.2f) into.Add(BoxOn(post + to * 0.5f, to, to.magnitude * 0.5f, 0.25f, 0.2f, "a billboard's beam"));
                        }
                        break;
                    case CitySigns.Kind.Gantry:
                        for (int i = sg.firstFace; i < sg.firstFace + sg.faces; i++)
                        {
                            var fc = st.faces[i];
                            var n = new Vector3(fc.normal.x, 0f, fc.normal.z).normalized;
                            into.Add(BoxOn(fc.centre - n * 0.08f, Vector3.Cross(Vector3.up, n), fc.w * 0.5f, fc.h * 0.5f, 0.08f, "a gantry panel"));
                        }
                        {
                            var la = new Vector3(sg.legA.x, sg.top - 0.55f, sg.legA.y);
                            var lb = new Vector3(sg.legB.x, sg.top - 0.55f, sg.legB.y);
                            if ((la - lb).sqrMagnitude < 0.01f) la = new Vector3(sg.pos.x, sg.top - 0.55f, sg.pos.y);   // a cantilever: to the carriageway at least
                            var d = lb - la;
                            if (d.magnitude > 0.2f) into.Add(BoxOn((la + lb) * 0.5f, d, d.magnitude * 0.5f + 0.35f, 0.55f, 0.55f, "a gantry truss"));
                        }
                        break;
                }
            }
        }

        /// <summary>Does a capsule (segment p0..p1, radius r) touch a box
        /// grown by r (a hair more than the capsule's Minkowski sum)?</summary>
        internal static bool CapsuleInBox(Vector3 p0, Vector3 p1, float r, SignBox b)
        {
            var d0 = p0 - b.c; var d1 = p1 - b.c;
            var a = new Vector3(Vector3.Dot(d0, b.x), Vector3.Dot(d0, b.y), Vector3.Dot(d0, b.z));
            var e = new Vector3(Vector3.Dot(d1, b.x), Vector3.Dot(d1, b.y), Vector3.Dot(d1, b.z));
            var h = new Vector3(b.hx + r, b.hy + r, b.hz + r);
            float t0 = 0f, t1 = 1f;
            var dir = e - a;
            for (int i = 0; i < 3; i++)
            {
                if (Mathf.Abs(dir[i]) < 1e-9f) { if (a[i] < -h[i] || a[i] > h[i]) return false; continue; }
                float u0 = (-h[i] - a[i]) / dir[i], u1 = (h[i] - a[i]) / dir[i];
                if (u0 > u1) { float tmp = u0; u0 = u1; u1 = tmp; }
                t0 = Mathf.Max(t0, u0); t1 = Mathf.Min(t1, u1);
                if (t0 > t1) return false;
            }
            return true;
        }

        static void SignAuditInner(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings)
        {
            float ts = CityMeshes.TileSize;
            var tiles = new List<(int tx, int tz, string why)>();
            var seen = new HashSet<long>();
            void Add(Vector2 at, string why)
            {
                int tx = Mathf.FloorToInt(at.x / ts), tz = Mathf.FloorToInt(at.y / ts);
                if (seen.Add(TileKey(tx, tz))) tiles.Add((tx, tz, why));
            }
            // the plan's shot spots (I-77, I-277, South Blvd) and the strip, with neighbours
            foreach (var s in SignSpots)
            {
                var c = LLtoGame(s.lat, s.lon);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++) Add(c + new Vector2(dx, dz) * ts, s.id);
            }
            int spotTiles = tiles.Count;
            // every tile a route runs through, 45 m either side (a post stands
            // at most that far off its carriageway): the density's truth
            var routeKm = new double[CitySignData.Routes.Length];
            foreach (var e in map.edges)
            {
                if (e.link || e.tunnel) continue;
                int ri = CitySignData.RouteOf(e.wayId);
                if (ri < 0) continue;
                routeKm[ri] += e.length / 1000.0 * (e.oneway ? 0.5 : 1.0);
                for (float s = 0f; s <= e.length + 1f; s += 32f)
                {
                    float sc = Mathf.Min(s, e.length);
                    var p = e.PointAt(sc);
                    var t = e.TangentAt(sc);
                    var n = new Vector2(t.y, -t.x);
                    Add(p, "route"); Add(p + n * 45f, "route"); Add(p - n * 45f, "route");
                }
            }
            // and every city race route's tiles (their run-off)
            if (map.routes != null)
                foreach (var r in map.routes)
                    foreach (int ei in r.edges)
                    {
                        var e = map.edges[ei];
                        for (float s = 0f; s <= e.length; s += 32f) Add(e.PointAt(s), "race " + r.id);
                    }

            var bad = new Dictionary<string, int>();
            var badWhere = new Dictionary<string, string>();
            void Bad(string what, Vector2 at, string detail)
            {
                bad.TryGetValue(what, out int n);
                bad[what] = n + 1;
                if (n == 0) badWhere[what] = $"({at.x:0.0},{at.y:0.0}) {LatLon(at.x, at.y)} {detail}";
            }
            var perRoute = new int[CitySignData.Routes.Length];
            int bulletins = 0, posters = 0, poles = 0, polePoi = 0, gantries = 0, osm = 0, posts = 0, lamps = 0, faces = 0, cantilevers = 0;
            int refusedB = 0, refusedP = 0, refusedG = 0, multiSub = 0, outside = 0, runOff = 0, vetoed = 0, foreign = 0;
            int tilesWith = 0, faceBad = 0, lampBad = 0;
            float worstDot = 1f, worstClear = float.MaxValue, msSum = 0f, msMax = 0f;
            string worstDotAt = "", worstClearAt = "";
            bool same = true;
            var all = new Dictionary<long, (CitySigns.Kind kind, Vector2 p)>();
            int twice = 0;
            // the ground signs take in OTHER tiles than their own, and their
            // feet there: checked against those tiles' own masks and trees
            var awayMarks = new Dictionary<long, List<(CitySigns.Mark m, CitySigns.Kind kind)>>();
            var awayFeet = new Dictionary<long, List<(Vector2 f, CitySigns.Kind kind)>>();
            // every solid post, for the clash check
            var postCells = new Dictionary<long, List<(Vector2 c, float r, int sign)>>();
            int signId = 0, postId = 0, awayFeetN = 0, treesOn = 0, clashes = 0;
            int pLost = 0, pNoStore = 0, pAtKerb = 0, pNoDriver = 0, pNoGround = 0, pBend = 0;
            float storeM = 0f;
            // the WP-15 review: wires and poles against the signs, in 3D
            var signBoxes = new List<SignBox>();
            var furnPoles = new List<CityPoles.Pole>();
            var furnSpans = new List<(CityPoles.Pole a, CityPoles.Pole b)>();
            var furnCaps = new List<(Vector3 a, Vector3 b, float r, bool wire)>();
            var wireList = new List<(Vector3 from, Vector3 to, float sag, float halfW)>();
            var partList = new List<(Vector3 a, Vector3 b, float r)>();
            int wireHits = 0, partHits = 0, boxesNearPoles = 0, boxesAll = 0, cabWireSteps = 0, boardWireSteps = 0;
            float nearestAir = float.MaxValue;
            string nearestAirAt = "";
            var hitKinds = new Dictionary<string, int>();
            // the streets at the shot spots: their business signs, and their length (each side)
            var stripSigns = new Dictionary<string, int>();
            var stripM = new Dictionary<string, float>();
            // why the business signs at the shot spots did or did not stand, by street
            var poleWhy = new Dictionary<string, Dictionary<string, int>>();
            var frontSegs = new HashSet<int>();
            var frontEdges = new SortedSet<int>();
            float frontM = 0f;
            var tileSet = new HashSet<long>();
            foreach (var t in tiles) tileSet.Add(TileKey(t.tx, t.tz));
            long KeyOf(Vector2 p) => TileKey(Mathf.FloorToInt(p.x / ts), Mathf.FloorToInt(p.y / ts));
            void TreesOnMarks(IEnumerable<(CitySigns.Mark m, CitySigns.Kind kind)> marks, CityTrees.TreeTile tile)
            {
                foreach (var (m, kind) in marks)
                {
                    float r = m.r - RoadsideOccupancy.CellPadM;
                    foreach (var tr in tile.trees)
                    {
                        var f = new Vector2(tr.foot.x, tr.foot.z);
                        if (RoadsideOccupancy.DistToSeg(f, m.a, m.b) < r) { treesOn++; Bad("a tree on a sign's ground", f, $"{kind} ({(m.a == m.b ? "post" : "line")} r {m.r:0.0})"); break; }
                    }
                }
            }

            for (int ti = 0; ti < tiles.Count; ti++)
            {
                var (tx, tz, why) = tiles[ti];
                var tm = CityMeshes.Build(map, trims, buildings, tx, tz);
                CityTrees.Enabled = true;
                if (ti < spotTiles)
                    CitySigns.PoleTrace = (ei, at, outcome) =>
                    {
                        string nm = map.edges[ei].name ?? "";
                        if (!poleWhy.TryGetValue(nm, out var d)) poleWhy[nm] = d = new Dictionary<string, int>();
                        string k2 = outcome.StartsWith("fill no ground") || outcome.StartsWith("poi no ground") ? outcome.Substring(0, outcome.IndexOf("ground") + 6) : outcome;
                        d.TryGetValue(k2, out int n); d[k2] = n + 1;
                    };
                var tt = CityTrees.Build(map, trims, buildings, tm, tx, tz);
                CitySigns.PoleTrace = null;
                CityTrees.Enabled = false;
                var st = tt.signs;
                if (st == null) { DiscardMeshes(tm); continue; }
                vetoed += st.vetoed; foreign += st.foreign;
                storeM += st.storeFrontM;
                pLost += st.poleLost; pNoStore += st.poleNoStore; pAtKerb += st.poleAtKerb; pNoDriver += st.poleNoDriver; pNoGround += st.poleNoGround; pBend += st.poleBend;
                bool spot = ti < spotTiles;
                msSum += st.ms; msMax = Mathf.Max(msMax, st.ms);
                // the mask as it stood before the signs took their ground
                var fresh = RoadsideOccupancy.Build(map, trims, buildings, tm, tx, tz);
                var again = CityTrees.Build(map, trims, buildings, tm, tx, tz).signs;
                if (again == null || again.signs.Count != st.signs.Count) same = false;
                else for (int i = 0; i < st.signs.Count && same; i++)
                        if ((again.signs[i].pos - st.signs[i].pos).sqrMagnitude > 1e-6f || again.signs[i].kind != st.signs[i].kind) same = false;
                if (again != null && again.mesh != null) Object.DestroyImmediate(again.mesh);

                if (st.signs.Count > 0) tilesWith++;
                if (st.mesh != null && st.mesh.subMeshCount != 1) multiSub++;
                bulletins += st.bulletins; posters += st.posters; poles += st.poleSigns; gantries += st.gantries; osm += st.osm;
                posts += st.posts.Count; lamps += st.lamps.Count; faces += st.faces.Count;
                refusedB += st.refusedBoards; refusedP += st.refusedPoleSigns; refusedG += st.refusedGantries;
                var min = new Vector2(tx * ts, tz * ts);
                var max = min + Vector2.one * ts;

                // the trees keep off every bit of ground this tile's signs take
                // here; what they take in other tiles is checked there
                var here = new List<(CitySigns.Mark, CitySigns.Kind)>();
                foreach (var m in st.marks)
                {
                    here.Add((m, CitySigns.Kind.PoleSign));
                    if (m.local) continue;
                    var lo = Vector2.Min(m.a, m.b) - Vector2.one * m.r;
                    var hi = Vector2.Max(m.a, m.b) + Vector2.one * m.r;
                    for (int bz = Mathf.FloorToInt(lo.y / ts); bz <= Mathf.FloorToInt(hi.y / ts); bz++)
                        for (int bx = Mathf.FloorToInt(lo.x / ts); bx <= Mathf.FloorToInt(hi.x / ts); bx++)
                        {
                            if (bx == tx && bz == tz) continue;
                            long k = TileKey(bx, bz);
                            if (!awayMarks.TryGetValue(k, out var l)) awayMarks[k] = l = new List<(CitySigns.Mark, CitySigns.Kind)>();
                            l.Add((m, CitySigns.Kind.Gantry));
                        }
                }
                TreesOnMarks(here, tt);

                foreach (var sg in st.signs)
                {
                    signId++;
                    if (sg.kind == CitySigns.Kind.PoleSign && sg.osm) polePoi++;
                    if (spot && sg.kind == CitySigns.Kind.PoleSign)
                    {
                        string nm = map.edges[sg.edge].name ?? "";
                        stripSigns.TryGetValue(nm, out int sn); stripSigns[nm] = sn + 1;
                    }
                    // one owner
                    if (sg.owner.x < min.x || sg.owner.y < min.y || sg.owner.x >= min.x + ts || sg.owner.y >= min.y + ts) { outside++; Bad("owned by another tile", sg.pos, sg.kind.ToString()); }
                    long key = ((long)Mathf.RoundToInt(sg.pos.x * 2f) << 32) ^ (uint)Mathf.RoundToInt(sg.pos.y * 2f);
                    if (all.TryGetValue(key, out var prev) && prev.kind == sg.kind) { twice++; Bad("placed twice (a seam)", sg.pos, sg.kind.ToString()); }
                    else all[key] = (sg.kind, sg.pos);
                    if (sg.kind == CitySigns.Kind.Bulletin || sg.kind == CitySigns.Kind.Poster) { if (sg.route >= 0) perRoute[sg.route]++; }

                    // posts and legs, against the geometry and the mask before the signs
                    var feet = new List<Vector2>();
                    if (sg.kind == CitySigns.Kind.Gantry) { feet.Add(sg.legB); if ((sg.legA - sg.legB).sqrMagnitude > 0.01f) feet.Add(sg.legA); else cantilevers++; }
                    else feet.Add(sg.pos);
                    foreach (var f in feet)
                    {
                        float w = CitySigns.WorstRoad(map, trims, f, out int we, out string what);
                        if (w < -0.05f) Bad("a post " + what, f, $"{sg.kind} e{we} '{(we >= 0 ? map.edges[we].name : "")}' {w:0.00} m inside");
                        bool inTile = f.x >= min.x && f.y >= min.y && f.x < min.x + ts && f.y < min.y + ts;
                        if (inTile)
                        {
                            byte b = fresh.At(f);
                            if (b != 0) Bad("a post on a reserved cell of the mask", f, $"{sg.kind} bits {b} ({string.Join(", ", BitList(b))})");
                        }
                        else
                        {
                            // a leg or a post in the next tile: judged on that tile's own mask
                            long k = KeyOf(f);
                            if (!awayFeet.TryGetValue(k, out var l)) awayFeet[k] = l = new List<(Vector2, CitySigns.Kind)>();
                            l.Add((f, sg.kind));
                            awayFeetN++;
                        }
                        if (!map.FootprintClear(f, 0.6f)) Bad("a post in a real building", f, sg.kind.ToString());
                        if (map.InLake(f)) Bad("a post in a lake", f, sg.kind.ToString());
                        if (RaceRunOff.Inside(map, trims, f)) { runOff++; Bad("a post in race run-off", f, sg.kind.ToString()); }
                    }

                    for (int i = sg.firstFace; i < sg.firstFace + sg.faces; i++)
                    {
                        var fc = st.faces[i];
                        // turned to its traffic
                        var to = fc.viewer - fc.centre;
                        float dot = Vector3.Dot(fc.normal, to.normalized);
                        if (dot < worstDot) { worstDot = dot; worstDotAt = $"{fc.kind} ({fc.centre.x:0},{fc.centre.z:0}) {LatLon(fc.centre.x, fc.centre.z)}"; }
                        if (dot < FaceDotMin) { faceBad++; Bad("a face not turned to its traffic", P2(fc.centre), $"{fc.kind} dot {dot:0.000}"); }
                        var vw = new Vector2(fc.viewer.x, fc.viewer.z);
                        var se = map.edges[sg.edge];
                        string of = $"{fc.kind} of e{sg.edge} '{se.name}' cls{se.cls}{(se.oneway ? " one-way" : "")} at ({sg.pos.x:0.0},{sg.pos.y:0.0}), face {i - sg.firstFace}";
                        switch (CitySigns.DriverOn(map, trims, vw, P2(fc.centre), out string vwhat))
                        {
                            case 0: Bad("a face's driver is not on a road", vw, of); break;
                            case 1: Bad("a face's driver is driving away from it", vw, $"{of}; at the driver only {vwhat}"); break;
                        }
                        // footprint
                        var n2 = new Vector2(fc.normal.x, fc.normal.z).normalized;
                        var u = new Vector2(-n2.y, n2.x);
                        for (int k = 0; k <= 8; k++)
                        {
                            var q = P2(fc.centre) + u * ((k / 8f - 0.5f) * fc.w);
                            float top = CitySigns.RoadTopAt(map, trims, q, 0f);
                            if (fc.kind == CitySigns.Kind.Gantry)
                            {
                                if (float.IsNegativeInfinity(top)) continue;
                                float clear = fc.centre.y - fc.h * 0.5f - top;
                                if (clear < worstClear) { worstClear = clear; worstClearAt = $"({q.x:0},{q.y:0}) {LatLon(q.x, q.y)}"; }
                                if (clear < GantryClearMin) Bad("a gantry panel under 5.5 m over a road", q, $"{clear:0.00} m");
                            }
                            else if (!float.IsNegativeInfinity(top)) Bad("a sign face over pavement", q, fc.kind.ToString());
                        }
                    }
                    if ((sg.kind == CitySigns.Kind.Bulletin || sg.kind == CitySigns.Kind.Poster) && !sg.lit) lampBad++;
                }
                // NO WIRE, POLE, CROSSARM OR COBRA-HEAD THROUGH A SIGN (the
                // WP-15 review): every box a sign is drawn with, against every
                // wire (its drawn segments, sag and all) and every pole part
                // near it, whichever tile owns them
                cabWireSteps += st.poleWireSteps; boardWireSteps += st.boardWireSteps;
                SignBoxesOf(st, signBoxes);
                if (signBoxes.Count > 0)
                {
                    var blo = Vector2.one * float.MaxValue; var bhi = Vector2.one * float.MinValue;
                    foreach (var bx in signBoxes)
                    {
                        float rr = Mathf.Max(bx.hx, bx.hz);
                        blo = Vector2.Min(blo, P2(bx.c) - Vector2.one * rr); bhi = Vector2.Max(bhi, P2(bx.c) + Vector2.one * rr);
                    }
                    CityPoles.FurnitureNear(map, trims, buildings, blo - Vector2.one * 3f, bhi + Vector2.one * 3f, furnPoles, furnSpans);
                    furnCaps.Clear();
                    foreach (var p in furnPoles)
                    {
                        partList.Clear();
                        CityPoles.PartsOf(p, partList);
                        foreach (var (a, b, r) in partList) furnCaps.Add((a, b, r, false));
                    }
                    foreach (var (pa, pb) in furnSpans)
                    {
                        wireList.Clear();
                        CityPoles.WiresOf(pa, pb, wireList);
                        foreach (var w in wireList)
                            for (int i = 0; i < CityPoles.WireSegmentsDrawn; i++)
                                furnCaps.Add((CityPoles.WirePoint(w.from, w.to, w.sag, i / (float)CityPoles.WireSegmentsDrawn),
                                              CityPoles.WirePoint(w.from, w.to, w.sag, (i + 1) / (float)CityPoles.WireSegmentsDrawn), w.halfW, true));
                    }
                    foreach (var bx in signBoxes)
                    {
                        boxesAll++;
                        bool near = false, hit = false;
                        float reachB = Mathf.Sqrt(bx.hx * bx.hx + bx.hz * bx.hz);
                        foreach (var (a, b, r, wire) in furnCaps)
                        {
                            // plan distance from the box's centre to the capsule's line, less the box's reach
                            float air = RoadsideOccupancy.DistToSeg(P2(bx.c), P2(a), P2(b)) - reachB - r;
                            if (air > 5f) continue;
                            near = true;
                            if (!CapsuleInBox(a, b, r + SignWireAirM, bx)) continue;
                            hit = true;
                            if (wire) wireHits++; else partHits++;
                            hitKinds.TryGetValue(bx.what, out int hk); hitKinds[bx.what] = hk + 1;
                            Bad(wire ? "a utility wire through a sign" : "a utility pole, crossarm or cobra-head in a sign", P2(bx.c),
                                $"{bx.what} at ({bx.c.x:0.0},{bx.c.y:0.0},{bx.c.z:0.0}); {(wire ? "a wire" : "a pole part")} from y {a.y:0.0} to {b.y:0.0}");
                            break;
                        }
                        if (near) boxesNearPoles++;
                        if (!hit && near)
                        {
                            // how close the nearest part comes in plan (not a check)
                            foreach (var (a, b, r, wire) in furnCaps)
                            {
                                float air = RoadsideOccupancy.SegSegDistance(P2(a), P2(b), P2(bx.c) - new Vector2(bx.x.x, bx.x.z) * bx.hx, P2(bx.c) + new Vector2(bx.x.x, bx.x.z) * bx.hx) - bx.hz - r;
                                if (air < nearestAir) { nearestAir = air; nearestAirAt = $"{bx.what} ({bx.c.x:0},{bx.c.z:0}) {LatLon(bx.c.x, bx.c.z)} to a {(wire ? "wire" : "pole part")}"; }
                            }
                        }
                    }
                }

                // every solid post, for the clash check
                foreach (var p in st.posts)
                {
                    var c = new Vector2(p.centre.x, p.centre.z);
                    long k = ((long)Mathf.FloorToInt(c.x / 4f) << 32) ^ (uint)Mathf.FloorToInt(c.y / 4f);
                    if (!postCells.TryGetValue(k, out var l)) postCells[k] = l = new List<(Vector2, float, int)>();
                    l.Add((c, 0.5f * Mathf.Sqrt(p.size.x * p.size.x + p.size.z * p.size.z), ++postId));
                }
                // commercial frontage along the collectors and bigger in this tile (per side)
                frontSegs.Clear(); frontEdges.Clear();
                map.EdgeSegsInRect(min, max, frontSegs);
                foreach (int packed in frontSegs) frontEdges.Add(packed >> 12);
                foreach (int fei in frontEdges)
                {
                    var e = map.edges[fei];
                    if (e.link || e.tunnel || e.bridge || e.cls < 1 || e.cls > 4) continue;
                    for (float s = 4f; s < e.length; s += 8f)
                    {
                        var q = e.PointAt(s);
                        if (q.x < min.x || q.y < min.y || q.x >= max.x || q.y >= max.y) continue;
                        var n = new Vector2(e.TangentAt(s).y, -e.TangentAt(s).x);
                        float off = e.width * 0.5f + RoadsideOccupancy.ClearZoneOf(e) + 2.2f;
                        if (spot) { string nm = e.name ?? ""; stripM.TryGetValue(nm, out float sm); stripM[nm] = sm + 16f; }
                        if ((CitySignData.At(q + n * off) & CitySignData.Frontage) != 0) frontM += 8f;
                        if ((CitySignData.At(q - n * off) & CitySignData.Frontage) != 0) frontM += 8f;
                    }
                }
                if (st.mesh != null) Object.DestroyImmediate(st.mesh);
                if (tt.mesh != null) Object.DestroyImmediate(tt.mesh);
                DiscardMeshes(tm);
            }

            // THE NEXT TILES: every leg, post and bit of ground a sign takes
            // outside the tile that owns it, on that tile's own full mask (its
            // fill houses and lamps too) and against its own trees; and that
            // tile marked it (it found the same sign)
            var awayTiles = new SortedSet<long>(awayMarks.Keys);
            foreach (var k in awayFeet.Keys) awayTiles.Add(k);
            int awayUnmarked = 0;
            foreach (long k in awayTiles)
            {
                int tx = (int)(k >> 32), tz = unchecked((int)(k & 0xFFFFFFFFL));   // TileKey's own layout
                var tm = CityMeshes.Build(map, trims, buildings, tx, tz);
                var fresh = RoadsideOccupancy.Build(map, trims, buildings, tm, tx, tz);
                CityTrees.Enabled = true;
                var tt = CityTrees.Build(map, trims, buildings, tm, tx, tz);
                CityTrees.Enabled = false;
                if (awayFeet.TryGetValue(k, out var feet))
                    foreach (var (f, kind) in feet)
                    {
                        byte b = fresh.At(f);
                        if (b != 0) Bad("a post on a reserved cell of the next tile's mask", f, $"{kind} bits {b} ({string.Join(", ", BitList(b))})");
                        if (tt.occ == null || tt.occ.At(f) == 0) { awayUnmarked++; Bad("a post in the next tile that tile did not mark", f, kind.ToString()); }
                    }
                if (awayMarks.TryGetValue(k, out var marks)) TreesOnMarks(marks, tt);
                if (tt.signs != null && tt.signs.mesh != null) Object.DestroyImmediate(tt.signs.mesh);
                if (tt.mesh != null) Object.DestroyImmediate(tt.mesh);
                DiscardMeshes(tm);
            }

            // no two signs' posts in one another
            foreach (var kv in postCells)
                foreach (var (c, r, id) in kv.Value)
                {
                    int cx = (int)(kv.Key >> 32), cz = (int)(kv.Key & 0xFFFFFFFF);
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            long nk = ((long)(cx + dx) << 32) ^ (uint)(cz + dz);
                            if (!postCells.TryGetValue(nk, out var l)) continue;
                            foreach (var (c2, r2, id2) in l)
                                if (id2 != id && (c2.x > c.x || (c2.x == c.x && c2.y > c.y)) && Vector2.Distance(c, c2) < r + r2) { clashes++; Bad("two signs' posts in one another", c, $"{Vector2.Distance(c, c2):0.00} m apart"); }
                        }
                }

            // billboards counted by route: the OSM ones on their nearest route
            Line($"    {tiles.Count} tiles ({spotTiles} at the shot spots, the rest along the routes and the race routes), {tilesWith} with signs; " +
                 $"{bulletins} bulletins, {posters} posters ({osm} of them OSM's), {poles} business pole signs, {gantries} exit gantries ({cantilevers} cantilevers); " +
                 $"{posts} solid posts and legs, {lamps} floodlights, {faces} faces; place {msSum / Mathf.Max(1, tiles.Count):0.0} ms a tile (max {msMax:0.0})");
            Line($"    candidates that won their spacing but found no free ground: billboards {refusedB}, pole signs {refusedP}, gantries {refusedG}; " +
                 $"gantries and billboards dropped by their own tile on its fill houses or lamps: {vetoed}");
            Line($"    across the seams: {foreign} times a tile marked the ground of another tile's gantry or billboard; {awayFeetN} legs and posts in a tile not their own " +
                 $"({awayTiles.Count} such tiles rebuilt and checked: {awayUnmarked} feet that tile did not mark); trees on a sign's ground {treesOn}; posts in one another {clashes}");
            double frontKm = frontM / 1000.0;
            Line($"    business pole signs: {poles} ({polePoi} at an OSM business, {poles - polePoi} along the frontage) on {frontKm:0.0} km of commercial frontage (each side counted): " +
                 $"one every {(poles > 0 ? frontM / poles : 0f):0} m of it (the plan: every 30-60 m)");
            float everyM = poles > 0 ? storeM / poles : float.MaxValue;
            Check(everyM <= 60f, "a business sign every 30-60 m of store frontage (a store behind a collector or bigger, each side) (sign audit)",
                  $"{poles} on {storeM / 1000f:0.0} km: one every {everyM:0} m ({poles - polePoi} frontage fills alone: one every {(poles > polePoi ? storeM / (poles - polePoi) : 0f):0} m)");
            Line($"    business signs that did not stand: lost the spacing {pLost}, a frontage with no store behind {pNoStore}, a business or store at the kerb (its sign on its wall) {pAtKerb}, no drivers to turn to {pNoDriver}, no free ground {pNoGround}, a bend {pBend}");
            var strips = new List<string>(stripM.Keys);
            strips.Sort((a, b2) => stripM[b2].CompareTo(stripM[a]));
            var srow = new List<string>();
            foreach (var nm in strips)
            {
                if (srow.Count >= 10 || string.IsNullOrEmpty(nm)) continue;
                stripSigns.TryGetValue(nm, out int sn);
                srow.Add($"{nm} {sn} on {stripM[nm] / 1000f:0.0} km ({(sn > 0 ? stripM[nm] / sn : 0f):0} m)");
            }
            Line("    business signs along the shot spots' streets (each side counted; one every N m): " + string.Join("; ", srow));
            foreach (var nm in strips)
            {
                if (!poleWhy.TryGetValue(nm, out var d) || stripM[nm] < 800f) continue;
                var parts = new List<string>();
                foreach (var kv in d) parts.Add($"{kv.Key} {kv.Value}");
                Line($"      {nm}: " + string.Join("; ", parts));
            }
            foreach (var kv in bad) Line($"    {kv.Key}: {kv.Value}  e.g. {badWhere[kv.Key]}");
            int badTotal = 0;
            foreach (var kv in bad) if (!kv.Key.StartsWith("a face not turned") && !kv.Key.StartsWith("a utility")) badTotal += kv.Value;
            Check(bulletins + posters > 0 && poles > 0 && gantries > 0, "the city stands billboards, business pole signs and exit gantries (sign audit)", $"{bulletins + posters}, {poles}, {gantries}");
            Check(badTotal == 0, "no post on pavement, in a clear zone, under a deck, in a building, lake or run-off, or on a reserved cell of its own tile's mask or the next tile's; the next tile marks it; no tree on a sign's ground; no post in another; nothing over pavement but a gantry's panels, 5.5 m up (sign audit)", badTotal);
            Check(faceBad == 0, $"every face turned to its traffic: dot {FaceDotMin} or better to a driver {CitySigns.ViewAheadM:0} m up the road ({CitySigns.PoleViewM:0} m for a business's cabinet) (sign audit)", $"{faceBad} faces below; worst {worstDot:0.000} at {worstDotAt}");
            Line($"    gantry panels: the lowest {worstClear:0.00} m over the road under it, at {worstClearAt}");
            Check(outside == 0 && twice == 0, "every sign in the tile that placed it, none placed twice across a seam (sign audit)", $"{outside} outside, {twice} twice");
            Check(same, "a tile places the same signs every build (sign audit)");
            Check(multiSub == 0, "a tile's signs are one mesh with one material: at most +1 draw (sign audit)", multiSub);
            Check(lampBad == 0 && lamps >= 2 * bulletins + posters, "every billboard lit: two floodlights under a bulletin face, one under a poster (sign audit)", $"{lamps} lamps for {bulletins} bulletins, {posters} posters");
            Check(posts == 0 || posts >= bulletins + posters, "billboard monopoles and gantry legs are solid; pole-sign cabinets break away (Q15) (sign audit)", $"{posts} posts");
            var hitRow = new List<string>();
            foreach (var kv in hitKinds) hitRow.Add($"{kv.Key} {kv.Value}");
            Line($"    signs and the utility poles (WP-15 review, signs ON, poles ON): {boxesAll} sign boxes (cabinets, posts, faces, catwalks, beams, panels, trusses, legs), " +
                 $"{boxesNearPoles} within 5 m in plan of a wire or pole part; places stepped on from for a wire or pole: business cabinets {cabWireSteps}, billboards and gantries {boardWireSteps}; " +
                 $"closest a wire or pole part comes to a sign box in plan {(nearestAir < float.MaxValue ? $"{nearestAir:0.00} m ({nearestAirAt})" : "-")}" +
                 (hitRow.Count > 0 ? "; THROUGH: " + string.Join(", ", hitRow) : ""));
            Check(wireHits + partHits == 0 && boxesNearPoles > 0, $"no utility wire, pole, crossarm or cobra-head through or within {SignWireAirM:0.0} m of a sign's cabinet, posts, face, catwalk, beam, panel or truss, measured in 3D on the drawn wires (sign audit, the WP-15 review)",
                  $"{wireHits} wires, {partHits} pole parts through a sign; {boxesNearPoles} sign boxes within 5 m of one");

            // density per route, and per class
            double[] clsKm = new double[3], clsWant = new double[3], clsGot = new double[3];
            var rows = new List<string>();
            for (int r = 0; r < CitySignData.Routes.Length; r++)
            {
                var rt = CitySignData.Routes[r];
                if (routeKm[r] < 3.0) continue;
                double want = rt.density * routeKm[r];
                clsKm[rt.cls] += routeKm[r]; clsWant[rt.cls] += want; clsGot[rt.cls] += perRoute[r];
                rows.Add($"{rt.name} {perRoute[r]}/{want:0} ({perRoute[r] / routeKm[r]:0.00} per km of {routeKm[r]:0} km, NCDOT {rt.density:0.00})");
            }
            Line("    billboards per route (placed / NCDOT's figure): " + string.Join("; ", rows));
            for (int c = 1; c <= 2; c++)
            {
                if (clsKm[c] < 1.0) continue;
                double ratio = clsWant[c] > 0 ? clsGot[c] / clsWant[c] : 1.0;
                Check(System.Math.Abs(ratio - 1.0) <= SignDensityTol,
                      $"billboard density on {(c == 1 ? "interstates" : "US and NC routes")} within +-{SignDensityTol * 100:0}% of NCDOT's (sign audit)",
                      $"{clsGot[c]:0} placed for {clsWant[c]:0} ({clsGot[c] / clsKm[c]:0.00} per km against {clsWant[c] / clsKm[c]:0.00}; {clsKm[c]:0} km)");
            }
            double mwKm = 0; foreach (var e in map.edges) if (!e.link && e.cls >= 5 && !e.tunnel) mwKm += e.length / 1000.0 * (e.oneway ? 0.5 : 1.0);
            Line($"    exit gantries {gantries} over {mwKm:0} km of motorway in the audited tiles' reach (NCDOT counts 1.01 per km; one per exit here; {refusedG} exits found no legs)");
        }

        static Vector2 P2(Vector3 p) => new Vector2(p.x, p.z);

        static IEnumerable<string> BitList(byte b)
        {
            for (int i = 0; i < 8; i++) if ((b & (1 << i)) != 0) yield return RoadsideOccupancy.BitNames[i];
        }

        /// <summary>Where the sign shots look (plan: I-77, I-277, South Blvd),
        /// and the Independence strip.</summary>
        static readonly (string id, double lat, double lon)[] SignSpots =
        {
            ("i-77 north", 35.2600, -80.8350), ("i-277 uptown", 35.2195, -80.8500), ("south blvd", 35.1930, -80.8680),
            ("independence strip", 35.16804, -80.74309), ("i-85 sugar creek", 35.2650, -80.7780),
        };
    }
}
