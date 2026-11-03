using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CITY TILE BAKE (phase 1 of Forza-style streaming, 2026-11-02): every
    /// Charlotte tile built by the ONE generator (CityMeshes.Build - the
    /// TileJob's own steps) and written by <see cref="CityTileCache.Write"/>,
    /// one file a tile, then a manifest stamped with the fingerprint of what
    /// built it. A cache, never a copy: the fingerprint covers
    ///   * the generator's sources (<see cref="CodeDirs"/>, <see cref="CodeFiles"/>
    ///     - line endings ignored) and the bake's own format,
    ///   * the city data it reads (<see cref="DataFiles"/>),
    ///   * the generator's environment switches (CityTileCache.Settings),
    /// so a change to any of them makes the bake STALE: the editor and a player
    /// built after it ignore it and build live, and the Win64 build
    /// (PSXBuildWin64) bakes again. Nothing here is ever committed (/CityTiles/
    /// is git-ignored); the bake lives in the project (sandbox) folder.
    ///
    ///   Unity.exe -batchmode -projectPath &lt;sandbox&gt; -executeMethod PSXRacing.EditorTools.CityTileBake.BakeFromCommandLine
    ///             [-psxTileDir &lt;dir&gt;] [-psxTileForce]   (PSX_TILECACHE_TILES=x,z;x,z... bakes only those)
    /// Report: PSXRacing_citytiles.txt at the project root.
    ///
    /// Building on it: a generator that starts reading a source file outside
    /// Scripts/City, or a data file, adds it to the lists below.
    /// </summary>
    [InitializeOnLoad]
    public static class CityTileBake
    {
        static CityTileBake() { CityTileCache.EditorFingerprint = Fingerprint; }

        static string ProjectDir => Path.GetDirectoryName(Application.dataPath);

        /// <summary>The generator's sources: every .cs under these (recursively).</summary>
        public static readonly string[] CodeDirs = { "Assets/PSXRacing/Scripts/City" };
        /// <summary>...and these files outside them that shape a tile.</summary>
        public static readonly string[] CodeFiles = { "Assets/PSXRacing/Scripts/RoadsideRules.cs" };
        /// <summary>The city data a tile is built from (folder, pattern).</summary>
        public static readonly (string dir, string pattern)[] DataFiles =
        {
            ("Assets/PSXRacing/Resources", "charlotte_*.bytes"),
            ("Assets/PSXRacing/Resources/CityCrowns", "*.bytes"),
        };

        // ---- the fingerprint ----------------------------------------------
        public static string Fingerprint() => Fingerprint(out _, out _);

        /// <summary>SHA-256 over the format, the sources, the data and the
        /// generator switches; <paramref name="code"/> / <paramref name="data"/>
        /// are the two halves' own hashes (the report says which moved).</summary>
        public static string Fingerprint(out string code, out string data)
        {
            var codeFiles = new List<string>();
            foreach (var d in CodeDirs)
            {
                string full = Path.Combine(ProjectDir, d);
                if (Directory.Exists(full)) codeFiles.AddRange(Directory.GetFiles(full, "*.cs", SearchOption.AllDirectories));
            }
            foreach (var f in CodeFiles) { string full = Path.Combine(ProjectDir, f); if (File.Exists(full)) codeFiles.Add(full); }
            var dataFiles = new List<string>();
            foreach (var (d, pat) in DataFiles)
            {
                string full = Path.Combine(ProjectDir, d);
                if (Directory.Exists(full)) dataFiles.AddRange(Directory.GetFiles(full, pat, SearchOption.TopDirectoryOnly));
            }
            code = HashFiles(codeFiles, true);
            data = HashFiles(dataFiles, false);
            using (var sha = SHA256.Create())
            {
                var all = Encoding.UTF8.GetBytes("format " + CityTileCache.Format + "\ncode " + code + "\ndata " + data + "\nsettings " + CityTileCache.Settings() + "\n");
                return Hex(sha.ComputeHash(all));
            }
        }

        static string HashFiles(List<string> files, bool text)
        {
            var rel = new List<(string rel, string full)>();
            foreach (var f in files) rel.Add((Path.GetFullPath(f).Substring(ProjectDir.Length + 1).Replace('\\', '/'), f));
            rel.Sort((a, b) => string.CompareOrdinal(a.rel, b.rel));
            using (var sha = SHA256.Create())
            {
                foreach (var (r, full) in rel)
                {
                    var name = Encoding.UTF8.GetBytes(r + "\n");
                    sha.TransformBlock(name, 0, name.Length, null, 0);
                    var bytes = File.ReadAllBytes(full);
                    if (text)
                    {
                        // line endings are a checkout's, not the code's
                        int n = 0;
                        for (int i = 0; i < bytes.Length; i++) if (bytes[i] != (byte)'\r') bytes[n++] = bytes[i];
                        sha.TransformBlock(bytes, 0, n, null, 0);
                    }
                    else sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
                    var len = BitConverter.GetBytes((long)bytes.Length);
                    sha.TransformBlock(len, 0, len.Length, null, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return Hex(sha.Hash);
            }
        }

        static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        // ---- the tiles ------------------------------------------------------
        /// <summary>Every tile of the city: the box round every road and
        /// footprint, a tile of margin round it (the ring a car at the edge
        /// streams), in rows.</summary>
        public static List<Vector2Int> AllTiles(CityMap map)
        {
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            foreach (var e in map.edges)
            {
                if (e?.pts == null) continue;
                foreach (var p in e.pts) { x0 = Mathf.Min(x0, p.x); z0 = Mathf.Min(z0, p.y); x1 = Mathf.Max(x1, p.x); z1 = Mathf.Max(z1, p.y); }
            }
            if (map.footprints != null && map.footprints.Length > 0)
            {
                var r = map.footprintBounds;
                x0 = Mathf.Min(x0, r.xMin); z0 = Mathf.Min(z0, r.yMin); x1 = Mathf.Max(x1, r.xMax); z1 = Mathf.Max(z1, r.yMax);
            }
            var list = new List<Vector2Int>();
            if (x0 > x1) return list;
            float T = CityMeshes.TileSize;
            int tx0 = Mathf.FloorToInt(x0 / T) - 1, tz0 = Mathf.FloorToInt(z0 / T) - 1, tx1 = Mathf.FloorToInt(x1 / T) + 1, tz1 = Mathf.FloorToInt(z1 / T) + 1;
            // a radius (PSX_TILECACHE_RADIUS_KM) round uptown, for a partial bake
            float rad = float.TryParse(Environment.GetEnvironmentVariable("PSX_TILECACHE_RADIUS_KM"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float km) ? km * 1000f : float.MaxValue;
            for (int tz = tz0; tz <= tz1; tz++)
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    var c = new Vector2((tx + 0.5f) * T, (tz + 0.5f) * T);
                    if (rad < float.MaxValue && (c - map.uptown).magnitude > rad) continue;
                    list.Add(new Vector2Int(tx, tz));
                }
            return list;
        }

        /// <summary>The bake in <paramref name="dir"/> is this editor's (same
        /// fingerprint, every file of its manifest there).</summary>
        public static bool IsFresh(string dir, out CityTileCache.Manifest m, out string why)
        {
            m = CityTileCache.Manifest.Load(dir);
            if (m == null) { why = "no manifest at " + dir; return false; }
            if (m.format != CityTileCache.Format) { why = "format " + m.format; return false; }
            string fp = Fingerprint(out string code, out string data);
            if (m.fingerprint != fp) { why = $"STALE: baked {Short(m.fingerprint)}, sources+data now {Short(fp)} (code {Short(code)}, data {Short(data)}, settings {CityTileCache.Settings()})"; return false; }
            foreach (var t in m.tiles)
                if (!File.Exists(CityTileCache.PathOf(dir, t.tx, t.tz))) { why = $"tile {t.tx},{t.tz} missing"; return false; }
            why = $"fresh: {m.tiles.Count} tiles, {m.Bytes / 1048576.0:0} MB ({Short(fp)})";
            return true;
        }

        static string Short(string s) => string.IsNullOrEmpty(s) ? "-" : s.Length > 12 ? s.Substring(0, 12) : s;

        /// <summary>Bake when stale (or <paramref name="force"/>): the Win64
        /// build's step. False when the bake failed.</summary>
        public static bool EnsureFresh(string dir, bool force, out string fingerprint, out string report)
        {
            fingerprint = Fingerprint();
            if (!force && IsFresh(dir, out _, out string why)) { report = "city tiles " + why; return true; }
            bool ok = Bake(dir, null, out report);
            CityTileCache.Reset();
            return ok;
        }

        [MenuItem("PSX Racing/City Tiles/Bake (when stale)")]
        public static void BakeMenu()
        {
            bool ok = EnsureFresh(CityTileCache.DefaultDir(), false, out _, out string report);
            Debug.Log("[CityTileBake] " + (ok ? "" : "FAILED ") + report);
        }

        public static void BakeFromCommandLine()
        {
            string dir = CityTileCache.DefaultDir();
            bool force = false;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-psxTileDir" && i + 1 < args.Length) dir = args[i + 1];
                if (args[i] == "-psxTileForce") force = true;
            }
            bool ok;
            string report;
            string only = Environment.GetEnvironmentVariable("PSX_TILECACHE_TILES");
            if (!string.IsNullOrEmpty(only))
            {
                var tiles = new List<Vector2Int>();
                foreach (var t in only.Split(';'))
                {
                    var p = t.Split(',');
                    if (p.Length == 2 && int.TryParse(p[0], out int x) && int.TryParse(p[1], out int z)) tiles.Add(new Vector2Int(x, z));
                }
                ok = Bake(dir, tiles, out report);
            }
            else ok = EnsureFresh(dir, force, out _, out report);
            File.WriteAllText(Path.Combine(ProjectDir, "PSXRacing_citytiles.txt"), (ok ? "CITY TILES OK\n" : "CITY TILES FAILED\n") + report + "\n");
            Debug.Log("[CityTileBake] " + report);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>The world a bake builds through (the trims, the lots, the
        /// warm tables), made the way the probes make it.</summary>
        static CityWorld MakeWorld(out GameObject go)
        {
            go = new GameObject("~cityTileBake") { hideFlags = HideFlags.DontSave };
            var world = go.AddComponent<CityWorld>();
            typeof(CityWorld).GetMethod("EnsureInit", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(world, null);
            return world;
        }

        /// <summary>Build and write <paramref name="only"/> (null: every tile
        /// of the city), then the manifest. A partial bake writes a manifest of
        /// just those tiles.</summary>
        public static bool Bake(string dir, IList<Vector2Int> only, out string report)
        {
            var sb = new StringBuilder();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // the format must know every field the generator returns
            var known = new HashSet<string>(CityTileCache.KnownFields);
            foreach (var f in typeof(CityMeshes.TileMeshes).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                if (!known.Contains(f.Name))
                {
                    report = $"TileMeshes.{f.Name} is not in the tile file: add it to CityTileCache (WriteBody, Read, KnownFields) - refusing to bake";
                    return false;
                }
            var map = CityMap.Get();
            if (map == null) { report = "no city data"; return false; }
            GameObject go = null;
            try
            {
                var world = MakeWorld(out go);
                var trims = CityWorld.SharedTrims;
                var blds = CityWorld.SharedBuildings;
                var tiles = only != null ? new List<Vector2Int>(only) : AllTiles(map);
                string fp = Fingerprint(out string code, out string data);
                Directory.CreateDirectory(dir);
                // no manifest while it is being written: an interrupted bake is never fresh
                string man = Path.Combine(dir, CityTileCache.ManifestName);
                if (File.Exists(man)) File.Delete(man);
                var m = new CityTileCache.Manifest
                {
                    format = CityTileCache.Format, fingerprint = fp, settings = CityTileCache.Settings(),
                    note = $"code {Short(code)} data {Short(data)} baked {DateTime.Now:s} unity {Application.unityVersion}",
                };
                var keep = new HashSet<string>();
                long bytes = 0, verts = 0, maxBytes = 0;
                double buildMs = 0, writeMs = 0, maxBuild = 0, standMs = 0;
                string maxTile = "";
                // each tile is built, then STOOD UP and PLANTED as play does it (its
                // signs, poles and trees) before it is written: the lattice it carries
                // then holds every corner its stand-up and its planting read, each
                // worked out in this tile's own build - a baked tile never recomputes
                // one under whatever the builder last built
                var standUp = typeof(CityWorld).GetMethod("StandUp", BindingFlags.NonPublic | BindingFlags.Instance);
                var dropTile = typeof(CityWorld).GetMethod("DropTile", BindingFlags.NonPublic | BindingFlags.Instance);
                var pendField = typeof(CityWorld).GetField("treesPending", BindingFlags.NonPublic | BindingFlags.Instance);
                bool wasOn = CityTileCache.Enabled;
                CityTileCache.Enabled = false;
                try
                {
                for (int i = 0; i < tiles.Count; i++)
                {
                    var t = tiles[i];
                    long key = ((long)t.x << 24) ^ (t.y & 0xFFFFFF);
                    var c = System.Diagnostics.Stopwatch.StartNew();
                    var tm = CityMeshes.Build(map, trims, blds, t.x, t.y);
                    double b = c.Elapsed.TotalMilliseconds;
                    buildMs += b;
                    if (b > maxBuild) { maxBuild = b; maxTile = t.x + "," + t.y; }
                    c.Restart();
                    foreach (var mesh in new[] { tm.ground, tm.roads, tm.barriers, tm.guardrails, tm.kerbs, tm.lampPosts, tm.banks, tm.water, tm.buildings, tm.buildingCollider, tm.structure })
                        if (mesh != null) verts += mesh.vertexCount;
                    var body = CityTileCache.Encode(tm);   // the generator's result as it returned it
                    writeMs += c.Elapsed.TotalMilliseconds;
                    c.Restart();
                    standUp.Invoke(world, new object[] { key, t.x, t.y, tm, b, 0, 0f });
                    Dictionary<long, float> lattice;
                    if (pendField.GetValue(world) is System.Collections.IDictionary pend && pend.Contains(key))
                    {
                        lattice = (((CityMeshes.TileMeshes tm, Dictionary<long, float> lattice))pend[key]).lattice;
                        for (int g = 0; g < 400 && world.PrepareSigns(key) != 0; g++) { }
                        world.PlantTrees(key);   // its reads land in the same dictionary
                    }
                    else lattice = CityMeshes.TakeLattice();   // nothing to plant: the build's own
                    standMs += c.Elapsed.TotalMilliseconds;
                    c.Restart();
                    long n = CityTileCache.Write(CityTileCache.PathOf(dir, t.x, t.y), t.x, t.y, body, lattice);
                    writeMs += c.Elapsed.TotalMilliseconds;
                    dropTile.Invoke(world, new object[] { key });   // its objects and meshes
                    bytes += n;
                    if (n > maxBytes) maxBytes = n;
                    m.tiles.Add((t.x, t.y, n));
                    keep.Add(CityTileCache.FileName(t.x, t.y));
                    if ((i + 1) % 100 == 0)
                    {
                        Debug.Log($"[CityTileBake] {i + 1}/{tiles.Count} tiles, {bytes / 1048576.0:0} MB, {clock.Elapsed.TotalSeconds:0} s");
                        // the guard the brief sets: the bake must fit the disk
                        if (bytes / (double)(i + 1) * tiles.Count > 6.0 * 1024 * 1024 * 1024)
                        { report = $"PROJECTED BAKE {bytes / (double)(i + 1) * tiles.Count / 1073741824.0:0.0} GB over 6 GB - stopped at tile {i + 1}"; return false; }
                    }
                }
                }
                finally { CityTileCache.Enabled = wasOn; }
                // tiles of an older bake that this one does not hold
                int removed = 0;
                if (only == null)
                    foreach (var f in Directory.GetFiles(dir, "t*.bin"))
                        if (!keep.Contains(Path.GetFileName(f))) { File.Delete(f); removed++; }
                foreach (var f in Directory.GetFiles(dir, "*.tmp")) File.Delete(f);
                m.Save(dir);
                CityTileCache.Reset();
                sb.AppendLine($"baked {tiles.Count} tiles into {dir} in {clock.Elapsed.TotalSeconds:0} s: {bytes / 1048576.0:0.0} MB ({bytes / Math.Max(1, tiles.Count) / 1024.0:0} KB a tile, largest {maxBytes / 1024.0:0} KB), {verts / 1e6:0.0} M vertices");
                sb.AppendLine($"  build {buildMs / 1000.0:0} s (avg {buildMs / Math.Max(1, tiles.Count):0} ms, worst {maxBuild:0} ms at {maxTile}), stand-up + planting {standMs / 1000.0:0} s, write {writeMs / 1000.0:0} s; {removed} stale files removed");
                sb.AppendLine($"  fingerprint {fp}  (code {Short(code)}, data {Short(data)}, settings {m.settings})");
                report = sb.ToString().TrimEnd();
                return true;
            }
            catch (Exception e)
            {
                report = "BAKE THREW: " + e;
                return false;
            }
            finally
            {
                if (go != null)
                {
                    var w = go.GetComponent<CityWorld>();
                    if (w != null) w.DropAll();
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
        }

        // ---- the player's stamp (PSXBuildWin64) ------------------------------
        const string StampAsset = "Assets/PSXRacing/Resources/" + CityTileCache.StampResource + ".txt";

        /// <summary>Resources/citytiles_stamp.txt for ONE BuildPlayer call: the
        /// fingerprint the player will ask its bake for.</summary>
        public static void WriteStamp(string fingerprint)
        {
            File.WriteAllText(Path.Combine(ProjectDir, StampAsset), fingerprint + "\n");
            AssetDatabase.ImportAsset(StampAsset, ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>...and gone again after it (a web build never carries one).</summary>
        public static void RemoveStamp()
        {
            if (File.Exists(Path.Combine(ProjectDir, StampAsset))) AssetDatabase.DeleteAsset(StampAsset);
        }

        /// <summary>The bake copied next to a player (its StreamingAssets);
        /// returns the bytes copied.</summary>
        public static long CopyTo(string src, string dst)
        {
            if (Directory.Exists(dst)) Directory.Delete(dst, true);
            Directory.CreateDirectory(dst);
            long n = 0;
            foreach (var f in Directory.GetFiles(src))
            {
                string name = Path.GetFileName(f);
                if (name.EndsWith(".tmp")) continue;
                File.Copy(f, Path.Combine(dst, name));
                n += new FileInfo(f).Length;
            }
            return n;
        }
    }
}
