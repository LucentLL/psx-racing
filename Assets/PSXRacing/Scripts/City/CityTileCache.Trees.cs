using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// THE TREE FRAME IN THE TILE FILE (format 2; city-tree-bake, 2026-11-03).
    /// The free-roam perf tour on the baked city (ring 4, 173 km/h) still had
    /// 22-58 ms frames, all of them a tile's TREE FRAME: one sign or pole
    /// decision (CityWorld.PrepareSigns: "a slice always decides one at least")
    /// or one step of the planting that ran far past the slice budget
    /// (CityWorld.RunTreeJob). The planting is the generator's like the tile's
    /// build is - the same trees, poles, signals and signs from the tile, its
    /// lattice and the city data - so the bake tool plants every tile as play
    /// does and the file carries what the planting returned (WriteTrees): a
    /// baked tile stands its frame up and plants nothing.
    ///
    /// What is carried is everything CityWorld.StandTrees and the Attach
    /// functions read, and every list and count of the frame's classes but the
    /// few the audits alone read (<see cref="TreeFields"/>' left-out names: the
    /// occupancy mask, the poles' span list, the signals' head census, the
    /// signs' ground marks, the timings). The trees' own mesh is not carried:
    /// their list is, and a worker rebuilds the cards with the planting's own
    /// arithmetic (<see cref="CityTrees.CardData"/>), so the tree mesh is the
    /// planted one bit for bit at a fraction of the bytes. Every other mesh is
    /// carried as built, its bounds too (the wires' and halos' are grown).
    ///
    /// The gate: <see cref="TreeHash"/> over everything carried and the trees'
    /// mesh (HitchProbe bake mode, -psx-tilecache-verify), and the whole
    /// stood-up tile (HitchProbe's FullSig).
    /// </summary>
    public static partial class CityTileCache
    {
        // ---- what the file carries of a tree frame ----------------------------
        /// <summary>Every instance field of the tree frame's classes, carried or
        /// left out on purpose (the audits' alone). The bake tool refuses a
        /// class with any other field (<see cref="UnknownTreeField"/>): it
        /// would be lost on the way - carry it in WriteTrees / ReadTrees, or
        /// name it left out.</summary>
        public static readonly (System.Type type, string[] carried, string[] leftOut)[] TreeFields =
        {
            (typeof(CityTrees.TreeTile),
                new[] { "tx", "tz", "lotPlanted", "trees", "mesh", "signs", "poles", "signals", "under", "wanted", "solids", "grown", "shrunk", "lined", "breakaway", "rejects" },
                new[] { "occ", "ms", "occMs" }),
            (typeof(CityTrees.Tree),
                new[] { "foot", "h", "w", "r", "yawDeg", "cell", "species", "road", "dbh", "solid" }, new string[0]),
            (typeof(CityUnderstory.UnderTile),
                new[] { "mesh", "strips", "clumps", "shrubs" }, new string[0]),
            (typeof(CityPoles.PoleTile),
                new[] { "tx", "tz", "poles", "heads", "headMercury", "mesh", "stations", "refused", "lampsDrawn", "wires", "refusedWhy", "spanLong", "spanRoad", "spanGround", "spanBlocked" },
                new[] { "spans", "ms" }),
            (typeof(CityPoles.Pole),
                new[] { "chain", "k", "edge", "s", "foot", "top", "outward", "along", "lens", "levels", "roadY" }, new string[0]),
            (typeof(CitySignals.SignalTile),
                new[] { "tx", "tz", "mesh", "lamps", "halos", "poles", "postBoxes", "postPoles", "junctions", "signalJunctions", "mastArms", "spanWires", "stopBars", "refused",
                        "leftArms", "poleHeads", "postHeads", "backSpans", "nearSupps", "haloLens" },
                new[] { "heads", "stops", "approachHeads", "diag", "ms" }),
            (typeof(CitySigns.SignTile),
                new[] { "tx", "tz", "signs", "mesh", "guideMesh", "bulletins", "posters", "poleSigns", "gantries", "osm", "guides", "refusedBoards", "refusedPoleSigns",
                        "refusedGantries", "poleLost", "poleNoStore", "poleAtKerb", "poleNoDriver", "poleNoGround", "poleBend", "poleWireSteps", "boardWireSteps", "storeFrontM",
                        "vetoed", "foreign", "faces", "posts", "lamps", "guideLamps" },
                new[] { "marks", "ms" }),
            (typeof(CitySigns.Sign),
                new[] { "kind", "pos", "ground", "bottom", "top", "route", "osm", "lit", "edge", "legA", "legB", "clearance", "firstFace", "faces", "firstPost", "postCount", "owner" },
                new string[0]),
            (typeof(CitySigns.Face),
                new[] { "centre", "normal", "viewer", "w", "h", "kind" }, new string[0]),
        };

        /// <summary>A field of the tree frame's classes the file neither carries
        /// nor leaves out by name ("Class.field"), or null.</summary>
        public static string UnknownTreeField()
        {
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            foreach (var (t, carried, left) in TreeFields)
                foreach (var f in t.GetFields(F))
                    if (System.Array.IndexOf(carried, f.Name) < 0 && System.Array.IndexOf(left, f.Name) < 0) return t.Name + "." + f.Name;
            return null;
        }

        // ---- write (the bake tool, main thread) -------------------------------
        /// <summary>A planted tree frame as the file carries it, encoded the
        /// moment the planting ends (before anything stands it up).</summary>
        public static byte[] EncodeTrees(CityTrees.TreeTile tt) => EncodeTrees(tt, false, -1);

        static byte[] EncodeTrees(CityTrees.TreeTile tt, bool withTreeMesh, int only)
        {
            using (var ms = new MemoryStream(1 << 16))
            {
                using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) WriteTrees(w, tt, withTreeMesh, only);
                return ms.ToArray();
            }
        }

        /// <summary>The gate's check of a tree frame: everything the file
        /// carries of it, and its trees' mesh (which a baked tile rebuilds),
        /// hashed.</summary>
        public static string TreeHash(CityTrees.TreeTile tt)
        {
            if (tt == null) return "-";
            var b = EncodeTrees(tt, true, -1);
            return b.Length + ":" + Fnv(b, b.Length).ToString("x16");
        }

        static readonly string[] TreeParts = { "trees", "under", "poles", "signals", "signs" };

        /// <summary>The same, a hash a part (the trees with their mesh, the
        /// understory, the poles, the signals, the signs): which part differs.</summary>
        public static string TreeHashParts(CityTrees.TreeTile tt)
        {
            if (tt == null) return "-";
            var sb = new System.Text.StringBuilder();
            for (int p = 0; p < TreeParts.Length; p++)
            {
                var b = EncodeTrees(tt, true, p);
                if (p > 0) sb.Append(' ');
                sb.Append(TreeParts[p]).Append('=').Append((Fnv(b, b.Length) & 0xFFFFFFFFUL).ToString("x8"));
            }
            return sb.ToString();
        }

        /// <summary>The frame, part by part (<paramref name="only"/>: one part, -1 all).</summary>
        static void WriteTrees(BinaryWriter w, CityTrees.TreeTile tt, bool withTreeMesh, int only)
        {
            if (only < 0 || only == 0) WriteTreeList(w, tt, withTreeMesh);
            if (only < 0 || only == 1) WriteUnder(w, tt.under);
            if (only < 0 || only == 2) WritePoles(w, tt.poles);
            if (only < 0 || only == 3) WriteSignals(w, tt.signals);
            if (only < 0 || only == 4) WriteSigns(w, tt.signs);
        }

        static void WriteTreeList(BinaryWriter w, CityTrees.TreeTile tt, bool withTreeMesh)
        {
            w.Write(tt.tx); w.Write(tt.tz);
            w.Write(tt.lotPlanted); w.Write(tt.wanted);
            w.Write(tt.solids); w.Write(tt.grown); w.Write(tt.shrunk); w.Write(tt.lined); w.Write(tt.breakaway);
            w.Write(tt.rejects.Length); foreach (int v in tt.rejects) w.Write(v);
            w.Write(tt.trees.Count);
            foreach (var t in tt.trees)
            {
                WriteV3(w, t.foot); w.Write(t.h); w.Write(t.w); w.Write(t.r); w.Write(t.yawDeg);
                w.Write(t.cell); w.Write((byte)t.species); w.Write(t.road); w.Write(t.dbh); w.Write(t.solid);
            }
            // (the hash only: the file rebuilds it from the list)
            if (withTreeMesh) WriteMesh(w, tt.mesh);
        }

        static void WriteUnder(BinaryWriter w, CityUnderstory.UnderTile ut)
        {
            w.Write(ut != null);
            if (ut == null) return;
            WriteMesh(w, ut.mesh);
            w.Write(ut.strips); w.Write(ut.clumps); w.Write(ut.shrubs);
        }

        static void WritePoles(BinaryWriter w, CityPoles.PoleTile pt)
        {
            w.Write(pt != null);
            if (pt == null) return;
            w.Write(pt.tx); w.Write(pt.tz);
            w.Write(pt.poles.Count);
            foreach (var p in pt.poles) WritePole(w, p);
            WriteList(w, pt.heads);
            w.Write(pt.headMercury.Count); foreach (bool b in pt.headMercury) w.Write(b);
            WriteMesh(w, pt.mesh);
            foreach (int v in new[] { pt.stations, pt.refused, pt.lampsDrawn, pt.wires, pt.spanLong, pt.spanRoad, pt.spanGround, pt.spanBlocked }) w.Write(v);
            w.Write(pt.refusedWhy.Length); foreach (int v in pt.refusedWhy) w.Write(v);
        }

        static void WritePole(BinaryWriter w, CityPoles.Pole p)
        {
            w.Write(p.chain); w.Write(p.k); w.Write(p.edge); w.Write(p.s);
            WriteV3(w, p.foot); w.Write(p.top); WriteV2(w, p.outward); WriteV2(w, p.along); WriteV3(w, p.lens);
            w.Write(p.levels); w.Write(p.roadY);
        }

        static void WriteSignals(BinaryWriter w, CitySignals.SignalTile st)
        {
            w.Write(st != null);
            if (st == null) return;
            w.Write(st.tx); w.Write(st.tz);
            WriteMesh(w, st.mesh); WriteMesh(w, st.lamps); WriteMesh(w, st.halos);
            w.Write(st.poles.Count);
            foreach (var (foot, top) in st.poles) { WriteV3(w, foot); w.Write(top); }
            w.Write(st.postBoxes.Count);
            foreach (var (c, size, yaw) in st.postBoxes) { WriteV3(w, c); WriteV3(w, size); w.Write(yaw); }
            // a set: written in order, so the same set is the same bytes
            var pp = new List<int>(st.postPoles);
            pp.Sort();
            w.Write(pp.Count); foreach (int i in pp) w.Write(i);
            w.Write(st.haloLens != null ? st.haloLens.Length : -1);
            if (st.haloLens != null) foreach (ushort c in st.haloLens) w.Write(c);
            foreach (int v in new[] { st.junctions, st.signalJunctions, st.mastArms, st.spanWires, st.stopBars, st.refused, st.leftArms, st.poleHeads, st.postHeads, st.backSpans, st.nearSupps })
                w.Write(v);
        }

        static void WriteSigns(BinaryWriter w, CitySigns.SignTile sg)
        {
            w.Write(sg != null);
            if (sg == null) return;
            w.Write(sg.tx); w.Write(sg.tz);
            WriteMesh(w, sg.mesh); WriteMesh(w, sg.guideMesh);
            w.Write(sg.signs.Count);
            foreach (var s in sg.signs)
            {
                w.Write((byte)s.kind); WriteV2(w, s.pos); w.Write(s.ground); w.Write(s.bottom); w.Write(s.top); w.Write(s.route);
                w.Write(s.osm); w.Write(s.lit); w.Write(s.edge); WriteV2(w, s.legA); WriteV2(w, s.legB); w.Write(s.clearance);
                w.Write(s.firstFace); w.Write(s.faces); w.Write(s.firstPost); w.Write(s.postCount); WriteV2(w, s.owner);
            }
            w.Write(sg.faces.Count);
            foreach (var f in sg.faces) { WriteV3(w, f.centre); WriteV3(w, f.normal); WriteV3(w, f.viewer); w.Write(f.w); w.Write(f.h); w.Write((byte)f.kind); }
            w.Write(sg.posts.Count);
            foreach (var (c, size, yaw) in sg.posts) { WriteV3(w, c); WriteV3(w, size); w.Write(yaw); }
            WriteList(w, sg.lamps);
            WriteList(w, sg.guideLamps);
            foreach (int v in new[] { sg.bulletins, sg.posters, sg.poleSigns, sg.gantries, sg.osm, sg.guides, sg.refusedBoards, sg.refusedPoleSigns, sg.refusedGantries,
                                      sg.poleLost, sg.poleNoStore, sg.poleAtKerb, sg.poleNoDriver, sg.poleNoGround, sg.poleBend, sg.poleWireSteps, sg.boardWireSteps,
                                      sg.vetoed, sg.foreign })
                w.Write(v);
            w.Write(sg.storeFrontM);
        }

        // ---- read (a worker thread: no Unity object is touched) ---------------
        /// <summary>A tile's tree frame read off disk: its TreeTile with every
        /// list and count (its meshes null), and the meshes as plain arrays
        /// (<see cref="MakeTrees"/> makes them) - the trees' rebuilt here.</summary>
        public sealed class TreeArrays
        {
            internal CityTrees.TreeTile tt;
            internal MeshArrays trees, under, poles, signals, lenses, halos, signs, guide;
            /// <summary>Its trees (the probe's count).</summary>
            public int Trees => tt != null ? tt.trees.Count : 0;
            /// <summary>Hand the arrays back without making meshes (a drop).</summary>
            public void Release()
            {
                trees?.Release(); under?.Release(); poles?.Release(); signals?.Release();
                lenses?.Release(); halos?.Release(); signs?.Release(); guide?.Release();
                trees = under = poles = signals = lenses = halos = signs = guide = null;
                tt = null;
            }
        }

        static TreeArrays ReadTrees(BinaryReader br, int tx, int tz)
        {
            var a = new TreeArrays();
            try
            {
                var tt = a.tt = new CityTrees.TreeTile { tx = br.ReadInt32(), tz = br.ReadInt32() };
                if (tt.tx != tx || tt.tz != tz) throw new IOException($"the tree frame is tile {tt.tx},{tt.tz}'s");
                tt.lotPlanted = br.ReadInt32(); tt.wanted = br.ReadSingle();
                tt.solids = br.ReadInt32(); tt.grown = br.ReadInt32(); tt.shrunk = br.ReadInt32(); tt.lined = br.ReadInt32(); tt.breakaway = br.ReadInt32();
                int n = br.ReadInt32();
                for (int i = 0; i < n; i++) { int v = br.ReadInt32(); if (i < tt.rejects.Length) tt.rejects[i] = v; }
                n = br.ReadInt32();
                tt.trees.Capacity = n;
                for (int i = 0; i < n; i++)
                    tt.trees.Add(new CityTrees.Tree
                    {
                        foot = ReadV3(br), h = br.ReadSingle(), w = br.ReadSingle(), r = br.ReadSingle(), yawDeg = br.ReadSingle(),
                        cell = br.ReadByte(), species = (CityTrees.Species)br.ReadByte(), road = br.ReadSingle(), dbh = br.ReadSingle(), solid = br.ReadBoolean(),
                    });
                // the trees' mesh: rebuilt from them by the planting's own code
                a.trees = CardArrays(tt);

                if (br.ReadBoolean())
                {
                    var ut = new CityUnderstory.UnderTile();
                    a.under = ReadMesh(br);
                    ut.strips = br.ReadInt32(); ut.clumps = br.ReadInt32(); ut.shrubs = br.ReadInt32();
                    tt.under = ut;
                }

                if (br.ReadBoolean())
                {
                    var pt = new CityPoles.PoleTile { tx = br.ReadInt32(), tz = br.ReadInt32() };
                    n = br.ReadInt32();
                    for (int i = 0; i < n; i++)
                        pt.poles.Add(new CityPoles.Pole
                        {
                            chain = br.ReadInt32(), k = br.ReadInt32(), edge = br.ReadInt32(), s = br.ReadSingle(),
                            foot = ReadV3(br), top = br.ReadSingle(), outward = ReadV2(br), along = ReadV2(br), lens = ReadV3(br),
                            levels = br.ReadInt32(), roadY = br.ReadSingle(),
                        });
                    ReadList(br, pt.heads);
                    n = br.ReadInt32();
                    for (int i = 0; i < n; i++) pt.headMercury.Add(br.ReadBoolean());
                    a.poles = ReadMesh(br);
                    pt.stations = br.ReadInt32(); pt.refused = br.ReadInt32(); pt.lampsDrawn = br.ReadInt32(); pt.wires = br.ReadInt32();
                    pt.spanLong = br.ReadInt32(); pt.spanRoad = br.ReadInt32(); pt.spanGround = br.ReadInt32(); pt.spanBlocked = br.ReadInt32();
                    n = br.ReadInt32();
                    for (int i = 0; i < n; i++) { int v = br.ReadInt32(); if (i < pt.refusedWhy.Length) pt.refusedWhy[i] = v; }
                    tt.poles = pt;
                }

                if (br.ReadBoolean())
                {
                    var st = new CitySignals.SignalTile { tx = br.ReadInt32(), tz = br.ReadInt32() };
                    a.signals = ReadMesh(br); a.lenses = ReadMesh(br); a.halos = ReadMesh(br);
                    n = br.ReadInt32();
                    for (int i = 0; i < n; i++) st.poles.Add((ReadV3(br), br.ReadSingle()));
                    n = br.ReadInt32();
                    for (int i = 0; i < n; i++) st.postBoxes.Add((ReadV3(br), ReadV3(br), br.ReadSingle()));
                    n = br.ReadInt32();
                    for (int i = 0; i < n; i++) st.postPoles.Add(br.ReadInt32());
                    n = br.ReadInt32();
                    if (n >= 0) { st.haloLens = new ushort[n]; for (int i = 0; i < n; i++) st.haloLens[i] = br.ReadUInt16(); }
                    st.junctions = br.ReadInt32(); st.signalJunctions = br.ReadInt32(); st.mastArms = br.ReadInt32(); st.spanWires = br.ReadInt32();
                    st.stopBars = br.ReadInt32(); st.refused = br.ReadInt32(); st.leftArms = br.ReadInt32(); st.poleHeads = br.ReadInt32();
                    st.postHeads = br.ReadInt32(); st.backSpans = br.ReadInt32(); st.nearSupps = br.ReadInt32();
                    tt.signals = st;
                }

                if (br.ReadBoolean())
                {
                    var sg = new CitySigns.SignTile { tx = br.ReadInt32(), tz = br.ReadInt32() };
                    a.signs = ReadMesh(br); a.guide = ReadMesh(br);
                    n = br.ReadInt32();
                    for (int i = 0; i < n; i++)
                        sg.signs.Add(new CitySigns.Sign
                        {
                            kind = (CitySigns.Kind)br.ReadByte(), pos = ReadV2(br), ground = br.ReadSingle(), bottom = br.ReadSingle(), top = br.ReadSingle(), route = br.ReadInt32(),
                            osm = br.ReadBoolean(), lit = br.ReadBoolean(), edge = br.ReadInt32(), legA = ReadV2(br), legB = ReadV2(br), clearance = br.ReadSingle(),
                            firstFace = br.ReadInt32(), faces = br.ReadInt32(), firstPost = br.ReadInt32(), postCount = br.ReadInt32(), owner = ReadV2(br),
                        });
                    n = br.ReadInt32();
                    for (int i = 0; i < n; i++)
                        sg.faces.Add(new CitySigns.Face { centre = ReadV3(br), normal = ReadV3(br), viewer = ReadV3(br), w = br.ReadSingle(), h = br.ReadSingle(), kind = (CitySigns.Kind)br.ReadByte() });
                    n = br.ReadInt32();
                    for (int i = 0; i < n; i++) sg.posts.Add((ReadV3(br), ReadV3(br), br.ReadSingle()));
                    ReadList(br, sg.lamps);
                    ReadList(br, sg.guideLamps);
                    sg.bulletins = br.ReadInt32(); sg.posters = br.ReadInt32(); sg.poleSigns = br.ReadInt32(); sg.gantries = br.ReadInt32(); sg.osm = br.ReadInt32();
                    sg.guides = br.ReadInt32(); sg.refusedBoards = br.ReadInt32(); sg.refusedPoleSigns = br.ReadInt32(); sg.refusedGantries = br.ReadInt32();
                    sg.poleLost = br.ReadInt32(); sg.poleNoStore = br.ReadInt32(); sg.poleAtKerb = br.ReadInt32(); sg.poleNoDriver = br.ReadInt32();
                    sg.poleNoGround = br.ReadInt32(); sg.poleBend = br.ReadInt32(); sg.poleWireSteps = br.ReadInt32(); sg.boardWireSteps = br.ReadInt32();
                    sg.vetoed = br.ReadInt32(); sg.foreign = br.ReadInt32();
                    sg.storeFrontM = br.ReadSingle();
                    tt.signs = sg;
                }
                return a;
            }
            catch { a.Release(); throw; }
        }

        [System.ThreadStatic] static List<Vector3> cardV, cardN;
        [System.ThreadStatic] static List<Vector2> cardUv;
        [System.ThreadStatic] static List<int> cardT;

        /// <summary>The trees' cards as mesh arrays (a worker), by
        /// <see cref="CityTrees.CardData"/>, as CityTrees.BuildMesh lays them;
        /// null with no tree (BuildMesh makes no mesh then).</summary>
        static MeshArrays CardArrays(CityTrees.TreeTile tt)
        {
            if (tt.trees.Count == 0) return null;
            if (cardV == null) { cardV = new List<Vector3>(4096); cardN = new List<Vector3>(4096); cardUv = new List<Vector2>(4096); cardT = new List<int>(6144); }
            CityTrees.CardData(tt.trees, tt.tx, tt.tz, cardV, cardN, cardUv, cardT);
            int n = cardV.Count;
            var a = new MeshArrays { name = CityTrees.MeshName, n = n, u32 = n > CityTrees.MaxU16Vertices, subs = 1, hasBounds = false };
            a.v = System.Buffers.ArrayPool<Vector3>.Shared.Rent(System.Math.Max(n, 1)); cardV.CopyTo(a.v);
            a.nrm = System.Buffers.ArrayPool<Vector3>.Shared.Rent(System.Math.Max(n, 1)); cardN.CopyTo(a.nrm);
            var uv = System.Buffers.ArrayPool<Vector2>.Shared.Rent(System.Math.Max(n, 1)); cardUv.CopyTo(uv);
            a.uvDim[0] = 2; a.uv[0] = uv;
            var ix = System.Buffers.ArrayPool<int>.Shared.Rent(System.Math.Max(cardT.Count, 1)); cardT.CopyTo(ix);
            a.idx = new[] { ix };
            a.idxN = new[] { cardT.Count };
            return a;
        }

        // ---- make (the main thread) -------------------------------------------
        /// <summary>The read tree frame's meshes made (main thread), its arrays
        /// handed back: the TreeTile is then the one the planting returns.</summary>
        public static CityTrees.TreeTile MakeTrees(TreeArrays ta)
        {
            if (ta == null || ta.tt == null) return null;
            var tt = ta.tt;
            tt.mesh = MakeOwned(ref ta.trees);
            if (tt.under != null) tt.under.mesh = MakeOwned(ref ta.under);
            if (tt.poles != null) tt.poles.mesh = MakeOwned(ref ta.poles);
            if (tt.signals != null)
            {
                tt.signals.mesh = MakeOwned(ref ta.signals);
                tt.signals.lamps = MakeOwned(ref ta.lenses);
                tt.signals.halos = MakeOwned(ref ta.halos);
            }
            if (tt.signs != null)
            {
                tt.signs.mesh = MakeOwned(ref ta.signs);
                tt.signs.guideMesh = MakeOwned(ref ta.guide);
            }
            ta.Release();   // (whatever a part without its owner left: nothing)
            return tt;
        }

        static Mesh MakeOwned(ref MeshArrays a)
        {
            if (a == null) return null;
            var m = Make(a);
            a.Release();
            a = null;
            return m;
        }
    }
}
