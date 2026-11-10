using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing.City
{
    /// <summary>
    /// THE BAKED CITY - phase 1 of Forza-style streaming (2026-11-02; owner:
    /// "games like Forza Horizon ... That's how this game should function",
    /// "this is not the final build of the city. make sure it can be easily
    /// built upon", load times fine if the result is seamless).
    ///
    /// A CACHE OF THE GENERATOR, never a copy of the city: the bake tool
    /// (Editor/CityTileBake.cs) runs the one generator (CityMeshes.Build, the
    /// TileJob's own steps) for every tile and writes what it returned - the
    /// whole <see cref="CityMeshes.TileMeshes"/> (every mesh with its slot
    /// lists, the lamps, solids, W-beams, masts, culverts, verges, houses...)
    /// and the lattice the build cached (the props, stations and trees stand
    /// on it) - one deflated file a tile, then a manifest stamped with a
    /// FINGERPRINT of the city data, the generator's sources, this format and
    /// the generator's environment switches. A bake whose fingerprint is not
    /// the running build's is ignored, and so is a missing tile: the tile is
    /// built live, as before. Nothing baked is ever committed.
    ///
    /// The runtime half (PC profile only - <see cref="PlatformProfile.PC"/>; a
    /// web player never opens a file here): CityWorld asks <see cref="Has"/>,
    /// a worker <see cref="Read"/>s the file into plain arrays, the main
    /// thread <see cref="Apply"/>s them into meshes, the colliders are cooked
    /// on a worker, and the tile is stood up by CityWorld.StandUp exactly as a
    /// built one is (lights, fades, colliders, props, trees...).
    ///
    /// Gate: Editor/HitchProbe.cs PSX_HITCH_MODE=bake stands every tile of its
    /// routes up BUILT and BAKED and compares the two, whole (every vertex,
    /// normal, UV, colour and index of every mesh under the tile, trees and
    /// furniture included).
    ///
    /// Switches: PSX_TILECACHE=0 or -psx-tilecache-off: never (the A/B);
    /// PSX_TILECACHE=1: outside the PC profile too (the editor's gate);
    /// PSX_TILECACHE_DIR / -psx-tilecache-dir &lt;dir&gt;: where the bake is
    /// (default: the player's StreamingAssets/CityTiles, the project's
    /// CityTiles in the editor).
    ///
    /// Building on it: a new field on TileMeshes must be written and read
    /// here (the bake tool refuses to bake a TileMeshes it does not know -
    /// <see cref="KnownFields"/>); anything a stand-up reads from the
    /// builder's statics must come with the tile the way the lattice does.
    /// </summary>
    public static class CityTileCache
    {
        /// <summary>The file layout. Part of the fingerprint: a change of it
        /// re-bakes (this file is among the generator's sources anyway).</summary>
        public const int Format = 2;   // 2: the median dress's plants (TileMeshes.medianPlants)
        public const string FolderName = "CityTiles";
        public const string ManifestName = "manifest.txt";
        /// <summary>The player's own fingerprint: Resources/citytiles_stamp.txt,
        /// written by the Win64 build (Editor/PSXBuildWin64.cs) for its one
        /// BuildPlayer call. A player without it has no bake.</summary>
        public const string StampResource = "citytiles_stamp";
        const uint Magic = 0x54585350;   // "PSXT"

        /// <summary>Off for the A/B: PSX_TILECACHE=0, -psx-tilecache-off.</summary>
        public static bool Enabled = System.Environment.GetEnvironmentVariable("PSX_TILECACHE") != "0" && !HasArg("-psx-tilecache-off");
        /// <summary>Use the bake outside the PC profile (the editor's gate).</summary>
        public static bool Force = System.Environment.GetEnvironmentVariable("PSX_TILECACHE") == "1";
        /// <summary>The editor's fingerprint of the sources and data it would
        /// build from (Editor/CityTileBake.cs sets it); null in a player.</summary>
        public static System.Func<string> EditorFingerprint;

        static int state = -1;   // -1 not opened, 0 no bake, 1 open
        static HashSet<long> tiles;
        static string dir, why = "not opened";

        /// <summary>A bake is open and matches this build.</summary>
        public static bool On
        {
            get
            {
                if (!Enabled || !(PlatformProfile.PC || Force)) return false;
                if (state < 0) Open();
                return state == 1;
            }
        }
        /// <summary>Why the bake is (not) in use, for the log.</summary>
        public static string Why => why;
        public static string Dir => dir ?? (dir = DefaultDir());
        public static int TileCount => tiles != null ? tiles.Count : 0;
        /// <summary>The tile is in the open bake.</summary>
        public static bool Has(int tx, int tz) => On && tiles.Contains(Key(tx, tz));
        /// <summary>Forget the open bake (a tool baked a new one).</summary>
        public static void Reset() { state = -1; tiles = null; dir = null; why = "not opened"; }

        static long Key(int tx, int tz) => ((long)tx << 24) ^ (tz & 0xFFFFFF);
        public static string FileName(int tx, int tz) => "t" + tx + "_" + tz + ".bin";
        public static string PathOf(string folder, int tx, int tz) => Path.Combine(folder, FileName(tx, tz));

        static bool HasArg(string a)
        {
            foreach (var s in System.Environment.GetCommandLineArgs())
                if (string.Equals(s, a, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string ArgAfter(string a)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], a, System.StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        public static string DefaultDir()
        {
            string d = System.Environment.GetEnvironmentVariable("PSX_TILECACHE_DIR");
            if (string.IsNullOrEmpty(d)) d = ArgAfter("-psx-tilecache-dir");
            if (!string.IsNullOrEmpty(d)) return d;
            if (Application.isEditor) return Path.Combine(Path.GetDirectoryName(Application.dataPath), FolderName);
            return Path.Combine(Application.streamingAssetsPath, FolderName);
        }

        // ---- the generator's environment (part of the fingerprint) ----------
        /// <summary>Environment switches that never shape a tile's meshes
        /// (streaming, trees, far layers, traffic, the tools): left out of the
        /// signature. Every other PSX_CITY_ / generator switch is in it, so a
        /// player run with one set builds live rather than show a bake made
        /// without it.</summary>
        static readonly HashSet<string> RuntimeOnly = new HashSet<string>
        {
            "PSX_CITY_SLICE", "PSX_CITY_AHEAD", "PSX_CITY_NOFORCE", "PSX_CITY_PACE", "PSX_CITY_URGENTTREES",
            "PSX_CITY_SLICETREES", "PSX_CITY_COURSE", "PSX_CITY_THREADS", "PSX_CITY_WARM", "PSX_CITY_CACHE",
            "PSX_CITY_PREUPLOAD", "PSX_CITY_BAKE", "PSX_CITY_FASTCOOK", "PSX_CITY_FADE", "PSX_CITY_TRAFFIC",
            "PSX_CITY_PROXIES", "PSX_CITY_FARCOVER", "PSX_CITY_FARFILL", "PSX_CITY_FARJAG", "PSX_CITY_FARLAND",
            "PSX_CITY_FARLIGHTSDAY", "PSX_CITY_FARPREFETCH", "PSX_CITY_FARSTAGE", "PSX_CITY_FARWORKER",
            "PSX_CITY_FUEL", "PSX_CITY_HTRACE",
        };
        static readonly string[] GeneratorPrefixes = { "PSX_CITY_", "PSX_BLD_", "PSX_CROWN_", "PSX_DECK_", "PSX_FAC_", "PSX_HEROES", "PSX_R11_" };

        /// <summary>The generator switches set in this process's environment,
        /// sorted NAME=value, or "-" for none.</summary>
        public static string Settings()
        {
            var l = new List<string>();
            foreach (System.Collections.DictionaryEntry kv in System.Environment.GetEnvironmentVariables())
            {
                string k = kv.Key as string;
                if (k == null || RuntimeOnly.Contains(k)) continue;
                bool gen = false;
                foreach (var p in GeneratorPrefixes) if (k.StartsWith(p, System.StringComparison.Ordinal)) { gen = true; break; }
                if (gen) l.Add(k + "=" + kv.Value);
            }
            if (l.Count == 0) return "-";
            l.Sort(System.StringComparer.Ordinal);
            return string.Join(";", l);
        }

        // ---- the manifest ---------------------------------------------------
        public sealed class Manifest
        {
            public int format;
            public string fingerprint = "", settings = "-", note = "";
            public readonly List<(int tx, int tz, long bytes)> tiles = new List<(int, int, long)>();
            public long Bytes { get { long b = 0; foreach (var t in tiles) b += t.bytes; return b; } }

            public static Manifest Load(string folder)
            {
                string p = Path.Combine(folder, ManifestName);
                if (!File.Exists(p)) return null;
                var m = new Manifest();
                foreach (var raw in File.ReadAllLines(p))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq > 0)
                    {
                        string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                        if (k == "format") int.TryParse(v, out m.format);
                        else if (k == "fingerprint") m.fingerprint = v;
                        else if (k == "settings") m.settings = v;
                        else if (k == "note") m.note = v;
                        continue;
                    }
                    var parts = line.Split(',');
                    if (parts.Length >= 3 && int.TryParse(parts[0], out int tx) && int.TryParse(parts[1], out int tz) && long.TryParse(parts[2], out long b))
                        m.tiles.Add((tx, tz, b));
                }
                return m;
            }

            public void Save(string folder)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# PSX Racing city tile bake (CityTileCache) - a build product, never committed");
                sb.AppendLine("format=" + format);
                sb.AppendLine("fingerprint=" + fingerprint);
                sb.AppendLine("settings=" + settings);
                sb.AppendLine("note=" + note);
                sb.AppendLine("tiles=" + tiles.Count);
                foreach (var t in tiles) sb.Append(t.tx).Append(',').Append(t.tz).Append(',').Append(t.bytes).AppendLine();
                string tmp = Path.Combine(folder, ManifestName + ".tmp"), dst = Path.Combine(folder, ManifestName);
                File.WriteAllText(tmp, sb.ToString());
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(tmp, dst);
            }
        }

        /// <summary>The fingerprint this build expects a bake to carry: the
        /// editor's own sources and data, or the player's stamp.</summary>
        public static string ExpectedFingerprint()
        {
            if (EditorFingerprint != null) return EditorFingerprint();
            var ta = Resources.Load<TextAsset>(StampResource);
            return ta != null ? ta.text.Trim() : null;
        }

        static void Open()
        {
            state = 0;
            tiles = null;
            dir = DefaultDir();
            try
            {
                var m = Manifest.Load(dir);
                if (m == null) { why = "no bake at " + dir; return; }
                if (m.format != Format) { why = $"bake format {m.format}, this build reads {Format}"; return; }
                string want = ExpectedFingerprint();
                if (string.IsNullOrEmpty(want)) { why = "this build carries no bake stamp"; return; }
                if (want != m.fingerprint) { why = $"STALE bake (fingerprint {Short(m.fingerprint)}, this build {Short(want)})"; return; }
                string s = Settings();
                if (s != m.settings) { why = $"bake made with generator switches '{m.settings}', running with '{s}'"; return; }
                tiles = new HashSet<long>();
                foreach (var t in m.tiles) tiles.Add(Key(t.tx, t.tz));
                state = 1;
                why = $"{tiles.Count} baked tiles, {m.Bytes / 1048576.0:0} MB at {dir} ({Short(want)})";
            }
            catch (System.Exception e) { why = "bake unreadable: " + e.Message; state = 0; }
            finally { Debug.Log("[CityTileCache] " + why); }
        }

        static string Short(string fp) => string.IsNullOrEmpty(fp) ? "-" : fp.Length > 12 ? fp.Substring(0, 12) : fp;

        // ==================================================================
        //  THE TILE FILE
        //  header: magic, format, tx, tz (16 bytes), then one deflate stream:
        //  the origin, the 11 meshes, the three slot lists, every list and
        //  count of TileMeshes, the lattice.
        // ==================================================================

        /// <summary>The meshes of a tile, in file order.</summary>
        public const int MeshCount = 12;
        static Mesh GetMesh(CityMeshes.TileMeshes tm, int i)
        {
            switch (i)
            {
                case 0: return tm.ground; case 1: return tm.roads; case 2: return tm.barriers; case 3: return tm.kerbs;
                case 4: return tm.water; case 5: return tm.banks; case 6: return tm.buildings; case 7: return tm.buildingCollider;
                case 8: return tm.lampPosts; case 9: return tm.guardrails; case 10: return tm.structure; default: return tm.medianPlants;
            }
        }
        static void SetMesh(CityMeshes.TileMeshes tm, int i, Mesh m)
        {
            switch (i)
            {
                case 0: tm.ground = m; break; case 1: tm.roads = m; break; case 2: tm.barriers = m; break; case 3: tm.kerbs = m; break;
                case 4: tm.water = m; break; case 5: tm.banks = m; break; case 6: tm.buildings = m; break; case 7: tm.buildingCollider = m; break;
                case 8: tm.lampPosts = m; break; case 9: tm.guardrails = m; break; case 10: tm.structure = m; break; default: tm.medianPlants = m; break;
            }
        }

        /// <summary>Every field of TileMeshes this file carries (or leaves
        /// out on purpose: the smoothness gate's tap). The bake tool refuses a
        /// TileMeshes with any other field: it would be lost on the way.</summary>
        public static readonly string[] KnownFields =
        {
            "origin", "ground", "groundSlots", "roads", "roadSlots", "barriers", "kerbs", "water", "banks",
            "buildings", "buildingSlots", "buildingCollider", "solids", "lamps", "deckDark", "lampPosts",
            "lampStations", "lampRejects", "wallFacingErrors", "footprintCount", "houseCount", "goreCount",
            "patchCount", "branchCount", "cutWallM", "cutWallByWhy", "cutWallByEdge", "footprintsCut",
            "footprintsLeftOut", "vergeSpans", "goreNoses", "vergeMetres", "railMetres", "wbeams", "wbeamPosts",
            "guardrails", "structure", "mastHeads", "mastFeet", "railEndsWBeam", "railEndsSloped", "railEndsInside",
            "railEndsConnected", "railEndsKept", "railEndsShort", "tap", "houseBoxes", "houseSeats", "mastTips",
            "culvertEnds", "medianPlants",
        };

        // ---- write (the bake tool, main thread) -----------------------------
        static readonly List<Vector3> wv3 = new List<Vector3>();
        static readonly List<Vector4> wv4 = new List<Vector4>();
        static readonly List<Vector2> wv2 = new List<Vector2>();
        static readonly List<Color32> wc = new List<Color32>();
        static readonly List<int> wi = new List<int>();

        /// <summary>A built tile's TileMeshes as the file carries it, encoded
        /// the moment the build returns it (before anything stands it up).</summary>
        public static byte[] Encode(CityMeshes.TileMeshes tm)
        {
            using (var ms = new MemoryStream(1 << 20))
            {
                using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) WriteBody(w, tm);
                return ms.ToArray();
            }
        }

        /// <summary>Write a tile: its encoded TileMeshes (<see cref="Encode"/>)
        /// and the lattice its build cached and its stand-up and planting read.
        /// Returns the file's size.</summary>
        public static long Write(string path, int tx, int tz, byte[] body, Dictionary<long, float> lattice)
        {
            string tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                var hw = new BinaryWriter(fs);
                hw.Write(Magic); hw.Write(Format); hw.Write(tx); hw.Write(tz);
                hw.Flush();
                using (var z = new DeflateStream(fs, System.IO.Compression.CompressionLevel.Optimal, true))
                using (var w = new BinaryWriter(new BufferedStream(z, 1 << 16)))
                {
                    w.Write(body);
                    int nl = lattice != null ? lattice.Count : 0;
                    w.Write(nl);
                    if (lattice != null) foreach (var kv in lattice) { w.Write(kv.Key); w.Write(kv.Value); }
                    w.Write(Magic);   // the end, read back as a check
                }
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            return new FileInfo(path).Length;
        }

        /// <summary>The gate's check: every field and mesh of a TileMeshes as
        /// the file carries it (the lattice left out), hashed.</summary>
        public static string DataHash(CityMeshes.TileMeshes tm)
        {
            if (tm == null) return "-";
            var b = Encode(tm);
            return b.Length + ":" + Fnv(b, b.Length).ToString("x16");
        }

        /// <summary>A lattice, order-free (sorted by key), hashed.</summary>
        public static string LatticeHash(Dictionary<long, float> lat)
        {
            if (lat == null) return "-";
            var keys = new List<long>(lat.Keys);
            keys.Sort();
            ulong h = 14695981039346656037UL;
            unchecked
            {
                foreach (var k in keys)
                {
                    h = (h ^ (ulong)k) * 1099511628211UL;
                    h = (h ^ (uint)System.BitConverter.SingleToInt32Bits(lat[k])) * 1099511628211UL;
                }
            }
            return keys.Count + ":" + h.ToString("x16");
        }

        static ulong Fnv(byte[] b, int n)
        {
            ulong h = 14695981039346656037UL;
            unchecked { for (int i = 0; i < n; i++) h = (h ^ b[i]) * 1099511628211UL; }
            return h;
        }

        static void WriteBody(BinaryWriter w, CityMeshes.TileMeshes tm)
        {
            WriteV3(w, tm.origin);
            for (int i = 0; i < MeshCount; i++) WriteMesh(w, GetMesh(tm, i));
            WriteSlots(w, tm.groundSlots); WriteSlots(w, tm.roadSlots); WriteSlots(w, tm.buildingSlots);
            w.Write(tm.solids.Count);
            foreach (var s in tm.solids) { WriteV3(w, s.center); WriteV3(w, s.size); w.Write(s.yawDeg); w.Write(s.round); w.Write(s.radius); }
            w.Write(tm.lamps.Count);
            foreach (var l in tm.lamps) { WriteV3(w, l.foot); WriteV3(w, l.head); w.Write(l.height); w.Write(l.kind); w.Write(l.breakaway); w.Write(l.mercury); }
            WriteList(w, tm.deckDark);
            foreach (int v in new[] { tm.lampStations, tm.wallFacingErrors, tm.footprintCount, tm.houseCount, tm.goreCount, tm.patchCount, tm.branchCount,
                                      tm.footprintsCut, tm.footprintsLeftOut, tm.railEndsWBeam, tm.railEndsSloped, tm.railEndsInside, tm.railEndsConnected,
                                      tm.railEndsKept, tm.railEndsShort })
                w.Write(v);
            w.Write(tm.lampRejects.Length); foreach (int v in tm.lampRejects) w.Write(v);
            w.Write(tm.cutWallM);
            w.Write(tm.cutWallByWhy.Length); foreach (float v in tm.cutWallByWhy) w.Write(v);
            w.Write(tm.cutWallByEdge.Count);
            foreach (var kv in tm.cutWallByEdge) { w.Write(kv.Key); w.Write(kv.Value.m); w.Write(kv.Value.why); WriteV2(w, kv.Value.at); }
            w.Write(tm.vergeSpans.Count);
            foreach (var s in tm.vergeSpans) { w.Write(s.edge); w.Write(s.side); w.Write(s.s0); w.Write(s.s1); }
            w.Write(tm.goreNoses.Count);
            foreach (var g in tm.goreNoses) { WriteV3(w, g.a); WriteV3(w, g.b); WriteV3(w, g.forward); w.Write(g.elevated); }
            w.Write(tm.vergeMetres); w.Write(tm.railMetres);
            w.Write(tm.wbeams.Count);
            foreach (var b in tm.wbeams)
            {
                WriteV3(w, b.a); WriteV3(w, b.b); WriteV2(w, b.outA); WriteV2(w, b.outB);
                w.Write(b.botA); w.Write(b.topA); w.Write(b.botB); w.Write(b.topB); w.Write(b.endA); w.Write(b.endB);
            }
            w.Write(tm.wbeamPosts.Count);
            foreach (var p in tm.wbeamPosts) { WriteV3(w, p.at); WriteV2(w, p.outward); w.Write(p.top); }
            WriteList(w, tm.mastHeads);
            w.Write(tm.mastFeet.Count); foreach (var f in tm.mastFeet) WriteV2(w, f);
            WriteList(w, tm.mastTips);
            w.Write(tm.houseBoxes.Count);
            foreach (var h in tm.houseBoxes) { WriteV2(w, h.c); WriteV2(w, h.u); w.Write(h.hu); w.Write(h.hv); }
            w.Write(tm.houseSeats.Count);
            foreach (var h in tm.houseSeats) { w.Write(h.kind); WriteV2(w, h.c); WriteV2(w, h.u); w.Write(h.hu); w.Write(h.hv); w.Write(h.y0); w.Write(h.floor); }
            w.Write(tm.culvertEnds.Count);
            foreach (var c in tm.culvertEnds)
            {
                WriteV2(w, c.at); WriteV2(w, c.outward); WriteV2(w, c.downstream);
                w.Write(c.groundY); w.Write(c.wallW); w.Write(c.wallH); w.Write(c.pipeD); w.Write(c.backfillM); w.Write(c.pastPave);
                w.Write(c.edge); w.Write(c.headwall); w.Write(c.backfillMet);
            }
        }

        static void WriteV3(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        static void WriteV2(BinaryWriter w, Vector2 v) { w.Write(v.x); w.Write(v.y); }
        static void WriteList(BinaryWriter w, List<Vector3> l) { w.Write(l.Count); foreach (var v in l) WriteV3(w, v); }
        static void WriteSlots(BinaryWriter w, CityMeshes.Slot[] s)
        {
            w.Write(s != null ? s.Length : -1);
            if (s != null) foreach (var x in s) w.Write((int)x);
        }
        static void WriteSpan<T>(BinaryWriter w, List<T> l) where T : struct
        {
            var arr = l.ToArray();
            w.Write(MemoryMarshal.AsBytes(new System.ReadOnlySpan<T>(arr)));
        }

        // attributes a tile mesh may carry (anything else: the bake refuses it)
        static readonly VertexAttribute[] UvAttr = { VertexAttribute.TexCoord0, VertexAttribute.TexCoord1, VertexAttribute.TexCoord2, VertexAttribute.TexCoord3 };

        static void WriteMesh(BinaryWriter w, Mesh m)
        {
            if (m == null) { w.Write(false); return; }
            w.Write(true);
            w.Write(m.name ?? "");
            int n = m.vertexCount;
            w.Write(n);
            w.Write(m.indexFormat == IndexFormat.UInt32);
            for (int a = (int)VertexAttribute.Position; a <= (int)VertexAttribute.BlendIndices; a++)
            {
                var at = (VertexAttribute)a;
                if (!m.HasVertexAttribute(at)) continue;
                var f = m.GetVertexAttributeFormat(at);
                int dim = m.GetVertexAttributeDimension(at);
                bool ok = at == VertexAttribute.Position || at == VertexAttribute.Normal ? f == VertexAttributeFormat.Float32 && dim == 3
                        : at == VertexAttribute.Tangent ? f == VertexAttributeFormat.Float32 && dim == 4
                        : at == VertexAttribute.Color ? f == VertexAttributeFormat.UNorm8 && dim == 4
                        : at >= VertexAttribute.TexCoord0 && at <= VertexAttribute.TexCoord3 ? f == VertexAttributeFormat.Float32 && dim >= 2
                        : false;
                if (!ok) throw new IOException($"mesh '{m.name}': vertex attribute {at} {f} x{dim} is not one the tile bake carries - extend CityTileCache.WriteMesh");
            }
            m.GetVertices(wv3); WriteSpan(w, wv3);
            bool hasN = m.HasVertexAttribute(VertexAttribute.Normal);
            w.Write(hasN);
            if (hasN) { m.GetNormals(wv3); WriteSpan(w, wv3); }
            bool hasT = m.HasVertexAttribute(VertexAttribute.Tangent);
            w.Write(hasT);
            if (hasT) { m.GetTangents(wv4); WriteSpan(w, wv4); }
            bool hasC = m.HasVertexAttribute(VertexAttribute.Color);
            w.Write(hasC);
            if (hasC) { m.GetColors(wc); WriteSpan(w, wc); }
            for (int ch = 0; ch < 4; ch++)
            {
                int dim = m.HasVertexAttribute(UvAttr[ch]) ? m.GetVertexAttributeDimension(UvAttr[ch]) : 0;
                w.Write((byte)dim);
                if (dim == 2) { m.GetUVs(ch, wv2); WriteSpan(w, wv2); }
                else if (dim == 3) { m.GetUVs(ch, wv3); WriteSpan(w, wv3); }
                else if (dim == 4) { m.GetUVs(ch, wv4); WriteSpan(w, wv4); }
            }
            w.Write(m.subMeshCount);
            bool u16 = m.indexFormat == IndexFormat.UInt16;
            for (int s = 0; s < m.subMeshCount; s++)
            {
                var sd = m.GetSubMesh(s);
                if (sd.topology != MeshTopology.Triangles || sd.baseVertex != 0)
                    throw new IOException($"mesh '{m.name}' submesh {s}: {sd.topology}, base vertex {sd.baseVertex} - the tile bake carries triangles from vertex 0");
                m.GetIndices(wi, s);
                w.Write(wi.Count);
                if (u16) { var us = new ushort[wi.Count]; for (int k = 0; k < us.Length; k++) us[k] = (ushort)wi[k]; w.Write(MemoryMarshal.AsBytes(new System.ReadOnlySpan<ushort>(us))); }
                else WriteSpan(w, wi);
            }
            var b = m.bounds;
            WriteV3(w, b.center); WriteV3(w, b.extents);
        }

        // ---- read (a worker thread: no Unity object is touched) -------------
        /// <summary>A tile read off disk: plain arrays, and the TileMeshes
        /// with everything but its meshes, which <see cref="Apply"/> makes.</summary>
        public sealed class Decoded
        {
            public int tx, tz;
            public CityMeshes.TileMeshes tm;
            public Dictionary<long, float> lattice;
            internal readonly MeshArrays[] meshes = new MeshArrays[MeshCount];
            /// <summary>The worker's read + inflate + decode, ms; the file's size.</summary>
            public double readMs;
            public long bytes;
            /// <summary>Why it could not be read (null: it was).</summary>
            public string error;
            /// <summary>Vertices over all its meshes.</summary>
            public int Vertices { get { int n = 0; foreach (var m in meshes) if (m != null) n += m.n; return n; } }
            /// <summary>Hand the arrays back without making meshes (a drop).</summary>
            public void Release() { foreach (var m in meshes) m?.Release(); }
        }

        internal sealed class MeshArrays
        {
            public string name;
            public int n;
            public bool u32;
            public Vector3[] v, nrm;
            public Vector4[] tan;
            public Color32[] col;
            public readonly byte[] uvDim = new byte[4];
            public readonly System.Array[] uv = new System.Array[4];
            public int subs;
            public int[][] idx;
            public int[] idxN;
            public Bounds bounds;

            public void Release()
            {
                Give(ref v); Give(ref nrm); Give(ref tan); Give(ref col);
                for (int c = 0; c < 4; c++)
                {
                    if (uv[c] is Vector2[] a2) System.Buffers.ArrayPool<Vector2>.Shared.Return(a2);
                    else if (uv[c] is Vector3[] a3) System.Buffers.ArrayPool<Vector3>.Shared.Return(a3);
                    else if (uv[c] is Vector4[] a4) System.Buffers.ArrayPool<Vector4>.Shared.Return(a4);
                    uv[c] = null;
                }
                if (idx != null) for (int s = 0; s < idx.Length; s++) if (idx[s] != null) { System.Buffers.ArrayPool<int>.Shared.Return(idx[s]); idx[s] = null; }
            }
            static void Give<T>(ref T[] a) { if (a != null) System.Buffers.ArrayPool<T>.Shared.Return(a); a = null; }
        }

        [System.ThreadStatic] static ushort[] u16Scratch;

        /// <summary>Read a tile's file (any thread). Never throws: a failure
        /// comes back as <see cref="Decoded.error"/> (the tile is then built live).</summary>
        public static Decoded Read(string folder, int tx, int tz)
        {
            var d = new Decoded { tx = tx, tz = tz };
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                string path = PathOf(folder, tx, tz);
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
                {
                    d.bytes = fs.Length;
                    var hr = new BinaryReader(fs);
                    if (hr.ReadUInt32() != Magic) throw new IOException("not a tile file");
                    int fmt = hr.ReadInt32();
                    if (fmt != Format) throw new IOException("format " + fmt);
                    if (hr.ReadInt32() != tx || hr.ReadInt32() != tz) throw new IOException("another tile's file");
                    using (var z = new DeflateStream(fs, CompressionMode.Decompress, true))
                    using (var r = new BinaryReader(new BufferedStream(z, 1 << 16)))
                    {
                        var tm = new CityMeshes.TileMeshes { origin = ReadV3(r) };
                        for (int i = 0; i < MeshCount; i++) d.meshes[i] = ReadMesh(r);
                        tm.groundSlots = ReadSlots(r); tm.roadSlots = ReadSlots(r); tm.buildingSlots = ReadSlots(r);
                        int n = r.ReadInt32();
                        for (int i = 0; i < n; i++)
                            tm.solids.Add(new CityMeshes.SolidBox { center = ReadV3(r), size = ReadV3(r), yawDeg = r.ReadSingle(), round = r.ReadBoolean(), radius = r.ReadSingle() });
                        n = r.ReadInt32();
                        for (int i = 0; i < n; i++)
                            tm.lamps.Add(new CityMeshes.Lamp { foot = ReadV3(r), head = ReadV3(r), height = r.ReadSingle(), kind = r.ReadByte(), breakaway = r.ReadBoolean(), mercury = r.ReadBoolean() });
                        ReadList(r, tm.deckDark);
                        tm.lampStations = r.ReadInt32(); tm.wallFacingErrors = r.ReadInt32(); tm.footprintCount = r.ReadInt32();
                        tm.houseCount = r.ReadInt32(); tm.goreCount = r.ReadInt32(); tm.patchCount = r.ReadInt32(); tm.branchCount = r.ReadInt32();
                        tm.footprintsCut = r.ReadInt32(); tm.footprintsLeftOut = r.ReadInt32(); tm.railEndsWBeam = r.ReadInt32();
                        tm.railEndsSloped = r.ReadInt32(); tm.railEndsInside = r.ReadInt32(); tm.railEndsConnected = r.ReadInt32();
                        tm.railEndsKept = r.ReadInt32(); tm.railEndsShort = r.ReadInt32();
                        n = r.ReadInt32();
                        for (int i = 0; i < n; i++) { int v = r.ReadInt32(); if (i < tm.lampRejects.Length) tm.lampRejects[i] = v; }
                        tm.cutWallM = r.ReadSingle();
                        n = r.ReadInt32();
                        for (int i = 0; i < n; i++) { float v = r.ReadSingle(); if (i < tm.cutWallByWhy.Length) tm.cutWallByWhy[i] = v; }
                        n = r.ReadInt32();
                        for (int i = 0; i < n; i++) { int k = r.ReadInt32(); float m = r.ReadSingle(); byte why = r.ReadByte(); var at = ReadV2(r); tm.cutWallByEdge[k] = (m, why, at); }
                        n = r.ReadInt32();
                        for (int i = 0; i < n; i++) tm.vergeSpans.Add((r.ReadInt32(), r.ReadInt32(), r.ReadSingle(), r.ReadSingle()));
                        n = r.ReadInt32();
                        for (int i = 0; i < n; i++) tm.goreNoses.Add((ReadV3(r), ReadV3(r), ReadV3(r), r.ReadBoolean()));
                        tm.vergeMetres = r.ReadSingle(); tm.railMetres = r.ReadSingle();
                        n = r.ReadInt32();
                        for (int i = 0; i < n; i++)
                            tm.wbeams.Add(new CityMeshes.WBeamPiece
                            {
                                a = ReadV3(r), b = ReadV3(r), outA = ReadV2(r), outB = ReadV2(r),
                                botA = r.ReadSingle(), topA = r.ReadSingle(), botB = r.ReadSingle(), topB = r.ReadSingle(), endA = r.ReadBoolean(), endB = r.ReadBoolean(),
                            });
                        n = r.ReadInt32();
                        for (int i = 0; i < n; i++) tm.wbeamPosts.Add(new CityMeshes.WBeamPost { at = ReadV3(r), outward = ReadV2(r), top = r.ReadSingle() });
                        ReadList(r, tm.mastHeads);
                        n = r.ReadInt32();
                        for (int i = 0; i < n; i++) tm.mastFeet.Add(ReadV2(r));
                        ReadList(r, tm.mastTips);
                        n = r.ReadInt32();
                        for (int i = 0; i < n; i++) tm.houseBoxes.Add((ReadV2(r), ReadV2(r), r.ReadSingle(), r.ReadSingle()));
                        n = r.ReadInt32();
                        for (int i = 0; i < n; i++)
                            tm.houseSeats.Add(new CityMeshes.HouseSeat { kind = r.ReadByte(), c = ReadV2(r), u = ReadV2(r), hu = r.ReadSingle(), hv = r.ReadSingle(), y0 = r.ReadSingle(), floor = r.ReadSingle() });
                        n = r.ReadInt32();
                        for (int i = 0; i < n; i++)
                            tm.culvertEnds.Add(new CityMeshes.CulvertEnd
                            {
                                at = ReadV2(r), outward = ReadV2(r), downstream = ReadV2(r),
                                groundY = r.ReadSingle(), wallW = r.ReadSingle(), wallH = r.ReadSingle(), pipeD = r.ReadSingle(),
                                backfillM = r.ReadSingle(), pastPave = r.ReadSingle(), edge = r.ReadInt32(), headwall = r.ReadBoolean(), backfillMet = r.ReadBoolean(),
                            });
                        n = r.ReadInt32();
                        var lat = new Dictionary<long, float>(System.Math.Max(n, 16));
                        for (int i = 0; i < n; i++) { long k = r.ReadInt64(); lat[k] = r.ReadSingle(); }
                        if (r.ReadUInt32() != Magic) throw new IOException("the tile file ends wrong");
                        d.tm = tm;
                        d.lattice = lat;
                    }
                }
            }
            catch (System.Exception e)
            {
                d.Release();
                d.tm = null; d.lattice = null;
                d.error = e.GetType().Name + ": " + e.Message;
            }
            d.readMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            return d;
        }

        static Vector3 ReadV3(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        static Vector2 ReadV2(BinaryReader r) => new Vector2(r.ReadSingle(), r.ReadSingle());
        static void ReadList(BinaryReader r, List<Vector3> l) { int n = r.ReadInt32(); for (int i = 0; i < n; i++) l.Add(ReadV3(r)); }
        static CityMeshes.Slot[] ReadSlots(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0) return null;
            var s = new CityMeshes.Slot[n];
            for (int i = 0; i < n; i++) s[i] = (CityMeshes.Slot)r.ReadInt32();
            return s;
        }

        static void ReadFully(BinaryReader r, System.Span<byte> dst)
        {
            while (dst.Length > 0)
            {
                int got = r.Read(dst);
                if (got <= 0) throw new EndOfStreamException();
                dst = dst.Slice(got);
            }
        }

        static T[] ReadArray<T>(BinaryReader r, int n) where T : struct
        {
            var a = System.Buffers.ArrayPool<T>.Shared.Rent(System.Math.Max(n, 1));
            ReadFully(r, MemoryMarshal.AsBytes(new System.Span<T>(a, 0, n)));
            return a;
        }

        static MeshArrays ReadMesh(BinaryReader r)
        {
            if (!r.ReadBoolean()) return null;
            var m = new MeshArrays { name = r.ReadString() };
            int n = m.n = r.ReadInt32();
            m.u32 = r.ReadBoolean();
            m.v = ReadArray<Vector3>(r, n);
            if (r.ReadBoolean()) m.nrm = ReadArray<Vector3>(r, n);
            if (r.ReadBoolean()) m.tan = ReadArray<Vector4>(r, n);
            if (r.ReadBoolean()) m.col = ReadArray<Color32>(r, n);
            for (int ch = 0; ch < 4; ch++)
            {
                byte dim = m.uvDim[ch] = r.ReadByte();
                if (dim == 2) m.uv[ch] = ReadArray<Vector2>(r, n);
                else if (dim == 3) m.uv[ch] = ReadArray<Vector3>(r, n);
                else if (dim == 4) m.uv[ch] = ReadArray<Vector4>(r, n);
                else if (dim != 0) throw new IOException("uv dimension " + dim);
            }
            m.subs = r.ReadInt32();
            m.idx = new int[m.subs][];
            m.idxN = new int[m.subs];
            for (int s = 0; s < m.subs; s++)
            {
                int c = m.idxN[s] = r.ReadInt32();
                var ix = System.Buffers.ArrayPool<int>.Shared.Rent(System.Math.Max(c, 1));
                if (m.u32) ReadFully(r, MemoryMarshal.AsBytes(new System.Span<int>(ix, 0, c)));
                else
                {
                    if (u16Scratch == null || u16Scratch.Length < c) u16Scratch = new ushort[System.Math.Max(c, 4096) * 3 / 2];
                    ReadFully(r, MemoryMarshal.AsBytes(new System.Span<ushort>(u16Scratch, 0, c)));
                    for (int k = 0; k < c; k++) ix[k] = u16Scratch[k];
                }
                m.idx[s] = ix;
            }
            m.bounds = new Bounds(ReadV3(r), ReadV3(r) * 2f);
            return m;
        }

        // ---- apply (the main thread) ----------------------------------------
        /// <summary>The decoded tile's meshes made (main thread), its arrays
        /// handed back. The TileMeshes is then the one a build returns.</summary>
        public static CityMeshes.TileMeshes Apply(Decoded d)
        {
            if (d == null || d.tm == null) return null;
            var tm = d.tm;
            for (int i = 0; i < MeshCount; i++)
            {
                var ma = d.meshes[i];
                if (ma == null) continue;
                SetMesh(tm, i, Make(ma));
                ma.Release();
                d.meshes[i] = null;
            }
            return tm;
        }

        static Mesh Make(MeshArrays a)
        {
            var m = new Mesh { name = a.name };
            if (a.u32) m.indexFormat = IndexFormat.UInt32;
            int n = a.n;
            m.SetVertices(a.v, 0, n);
            if (a.nrm != null) m.SetNormals(a.nrm, 0, n);
            if (a.tan != null) m.SetTangents(a.tan, 0, n);
            if (a.col != null) m.SetColors(a.col, 0, n);
            for (int ch = 0; ch < 4; ch++)
            {
                if (a.uv[ch] is Vector2[] u2) m.SetUVs(ch, u2, 0, n);
                else if (a.uv[ch] is Vector3[] u3) m.SetUVs(ch, u3, 0, n);
                else if (a.uv[ch] is Vector4[] u4) m.SetUVs(ch, u4, 0, n);
            }
            m.subMeshCount = a.subs;
            for (int s = 0; s < a.subs; s++)
                m.SetIndices(a.idx[s], 0, a.idxN[s], MeshTopology.Triangles, s, false, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>The meshes a tile build cooks ahead (CityMeshes.TileSteps'
        /// list, the colliders CityWorld.Attach gives a MeshCollider).</summary>
        public static Mesh[] ColliderMeshes(CityMeshes.TileMeshes tm) => new[]
        {
            tm.ground, tm.roads, tm.barriers, tm.guardrails,
            tm.buildings != null ? (tm.buildingCollider != null ? tm.buildingCollider : tm.buildings) : null,
        };
    }
}
