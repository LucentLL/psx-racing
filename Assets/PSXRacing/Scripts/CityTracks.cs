using System.Collections.Generic;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing
{
    /// <summary>
    /// THE CHARLOTTE RACES ADDED AFTER THE EXPORT'S OWN (owner, 2026-11-16: "I'd like to start adding
    /// race tracks (circuits, sprints, drag)"). The export's three routes live in charlotte_city.bytes;
    /// each new one is ITS OWN FILE, Resources/CityTracks/&lt;route id&gt;.json
    /// (tools/city/tracks/emit_track.mjs): the venue (menu name, blurb, laps, limit, the scene it races
    /// in, whether it has a reverse twin) and the route (a chain of the shipped graph's own edges, the
    /// menu's coarse line, the start line and the finish), with the hash of the graph it was routed on.
    /// Adding a track adds a file and edits nothing shared: no .cs, no index, no charlotte_*.bytes, so
    /// the baked city tiles stay valid (their fingerprint covers the bytes and the city code) and
    /// branches that each add tracks merge without a conflict.
    ///
    /// THE ORDER: a saved career stores its venue by index, so <see cref="TrackCatalog"/> appends these
    /// venues after every authored one in the order of Resources/CityTracks/_order.txt (the
    /// integrator's, tools/city/tracks/order.mjs), then any venue it does not list yet, by file name
    /// (the forward venue, then its "Rev" twin). The venue list does not depend on the switch below:
    /// the indices are the same with the tracks on or off.
    ///
    /// <see cref="Register"/> hands the routes to the parsed CityMap, so CityMode, the free-roam start
    /// gates and the editor's checks find them by id like the export's own. It refuses a file whose
    /// graph hash is not the map's: a re-export that moved the edges cannot send a race down the wrong
    /// road (re-run emit_track.mjs on the new graph). The menu reads the same files through
    /// <see cref="TryMenu"/> (TrackCatalog.EnsureRoute), never the city.
    /// </summary>
    public static class CityTracks
    {
        /// <summary>Resources/CityTracks/: one .json per track, plus _order.txt.</summary>
        public const string Folder = "CityTracks";
        public const string OrderName = "_order";

        /// <summary>DEFAULT ON (2026-11-16 evening: I485West and CltD1 raced to the finish, watched,
        /// sanctioned, with 0 closures on any race line). PSX_CITY_TRACKS=0 hides these
        /// races: none of them is offered or registered, the menu as it was (their venues stay in
        /// TrackCatalog.All for the saved indices).</summary>
        public static bool Enabled => System.Environment.GetEnvironmentVariable("PSX_CITY_TRACKS") != "0";

        [System.Serializable]
        public class RouteData
        {
            public string id = "", name = "";
            public int loop, oneway;
            public float roadWidth, speed, lengthM, startM, finishM;
            public float[] pts;
            public int[] edges, dirs;
        }

        /// <summary>What the menu and the race need besides the road (TrackCatalog builds the TrackDef).</summary>
        [System.Serializable]
        public class VenueData
        {
            public string id = "", name = "", blurb = "", label = "", host = "";
            public float roadWidth, speedLimitKmh;
            public int laps = 1, oneWay, reverse, noDelivery = 1;
            public string reverseName = "", reverseLabel = "", reverseBlurb = "";
        }

        [System.Serializable]
        public class TrackFile
        {
            public int version;
            public string graph = "", attribution = "", candidate = "";
            public VenueData venue;
            public RouteData route;
            [System.NonSerialized] public string file;
            [System.NonSerialized] public uint graphHash;
        }

        static List<TrackFile> files;
        static List<string> order;

        /// <summary>Every track file that parses, by file name. Loaded once; never depends on the switch.</summary>
        public static List<TrackFile> Files
        {
            get
            {
                if (files != null) return files;
                files = new List<TrackFile>();
                order = new List<string>();
                TextAsset[] assets;
                try { assets = Resources.LoadAll<TextAsset>(Folder); }
                catch (System.Exception e) { Debug.LogError("[CityTracks] Resources/" + Folder + ": " + e.Message); return files; }
                foreach (var ta in assets)
                {
                    if (ta == null) continue;
                    if (ta.name == OrderName)
                    {
                        foreach (var raw in ta.text.Split('\n'))
                        {
                            string line = raw;
                            int hash = line.IndexOf('#');
                            if (hash >= 0) line = line.Substring(0, hash);
                            line = line.Trim();
                            if (line.Length > 0) order.Add(line);
                        }
                        continue;
                    }
                    TrackFile t = null;
                    try { t = JsonUtility.FromJson<TrackFile>(ta.text); }
                    catch (System.Exception e) { Debug.LogError("[CityTracks] " + Folder + "/" + ta.name + ".json: " + e.Message); }
                    uint gh = 0;
                    string why = t == null ? "does not parse"
                        : t.venue == null || string.IsNullOrEmpty(t.venue.id) ? "has no venue id"
                        : t.route == null || string.IsNullOrEmpty(t.route.id) ? "has no route id"
                        : t.route.edges == null || t.route.dirs == null || t.route.edges.Length == 0 ||
                          t.route.edges.Length != t.route.dirs.Length ? "has no chain"
                        : !uint.TryParse(t.graph, System.Globalization.NumberStyles.HexNumber, null, out gh) ? "carries no graph hash"
                        : null;
                    if (why != null) { Debug.LogError("[CityTracks] " + Folder + "/" + ta.name + ".json " + why + ": left out"); continue; }
                    t.graphHash = gh;
                    t.file = ta.name;
                    files.Add(t);
                }
                files.Sort((a, b) => string.CompareOrdinal(a.file, b.file));
                return files;
            }
        }

        /// <summary>The venue ids of Resources/CityTracks/_order.txt, in its order.</summary>
        public static List<string> Order { get { var _ = Files; return order; } }

        /// <summary>Is <paramref name="routeId"/> one of these files' routes (and the tracks on)?</summary>
        public static bool Has(string routeId) => Find(routeId) != null;

        public static RouteData Find(string routeId)
        {
            var t = FileOf(routeId);
            return t != null ? t.route : null;
        }

        static TrackFile FileOf(string routeId)
        {
            if (!Enabled || string.IsNullOrEmpty(routeId)) return null;
            foreach (var t in Files) if (t.route.id == routeId) return t;
            return null;
        }

        /// <summary>The menu's facts for a route of these files: what charlotte_routes.json gives the
        /// export's own (TrackCatalog.EnsureRoute).</summary>
        public static bool TryMenu(string routeId, out RouteData route, out string attribution)
        {
            var t = FileOf(routeId);
            route = t != null ? t.route : null;
            attribution = t != null ? t.attribution ?? "" : "";
            return route != null && route.pts != null && route.pts.Length >= 4;
        }

        static CityMap registeredMap;

        /// <summary>
        /// Hand these files' routes to <paramref name="map"/> (once per map): appended to map.routes as
        /// CityMap.Route, each checked edge by edge (every edge exists, each one ends where the next
        /// begins, a loop closes). A file routed on another graph is left out.
        /// </summary>
        public static int Register(CityMap map)
        {
            if (map == null || map.edges == null) return 0;
            if (ReferenceEquals(map, registeredMap)) return 0;
            registeredMap = map;
            if (!Enabled || Files.Count == 0) return 0;
            var list = new List<CityMap.Route>(map.routes ?? new CityMap.Route[0]);
            int added = 0;
            foreach (var t in Files)
            {
                var r = t.route;
                if (map.RouteById(r.id) != null) continue;
                if (t.graphHash != map.graphHash)
                {
                    Debug.LogError($"[CityTracks] {Folder}/{t.file}.json was routed on graph {t.graphHash:x8}, this city is " +
                                   $"{map.graphHash:x8}: left out (re-run tools/city/tracks/emit_track.mjs on this graph)");
                    continue;
                }
                string why = Check(map, r);
                if (why != null) { Debug.LogError("[CityTracks] route " + r.id + " left out: " + why); continue; }
                var dirs = new sbyte[r.dirs.Length];
                for (int i = 0; i < dirs.Length; i++) dirs[i] = (sbyte)(r.dirs[i] < 0 ? -1 : 1);
                list.Add(new CityMap.Route
                {
                    id = r.id, name = r.name, loop = r.loop != 0, oneway = r.oneway != 0, roadWidth = r.roadWidth,
                    speedKmh = Mathf.RoundToInt(r.speed), lengthM = r.lengthM, startM = r.startM, finishM = r.finishM,
                    edges = (int[])r.edges.Clone(), dirs = dirs,
                });
                added++;
            }
            if (added > 0) map.routes = list.ToArray();
            return added;
        }

        static string Check(CityMap map, RouteData r)
        {
            int n = map.edges.Length;
            int prevEnd = -1, firstStart = -1;
            for (int i = 0; i < r.edges.Length; i++)
            {
                int ei = r.edges[i];
                if (ei < 0 || ei >= n) return "edge " + ei + " is not in this city";
                var e = map.edges[ei];
                int a = r.dirs[i] < 0 ? e.b : e.a, b = r.dirs[i] < 0 ? e.a : e.b;
                if (i == 0) firstStart = a;
                else if (a != prevEnd) return "the chain breaks at " + i + " (edge " + ei + ")";
                prevEnd = b;
            }
            if (r.loop != 0 && prevEnd != firstStart) return "the loop does not close";
            return null;
        }
    }
}
