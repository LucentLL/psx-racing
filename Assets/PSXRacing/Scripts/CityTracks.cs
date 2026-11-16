using System.Collections.Generic;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing
{
    /// <summary>
    /// THE CHARLOTTE RACES ADDED AFTER THE EXPORT'S OWN (owner, 2026-11-16: "I'd like to start adding
    /// race tracks (circuits, sprints, drag)"). The export's three routes live in charlotte_city.bytes;
    /// a new one lives in Resources/charlotte_tracks.json (tools/city/tracks/emit_tracks.mjs): a chain of
    /// the shipped graph's own edges, the menu's coarse line, the lap's start line, and the hash of the
    /// graph it was routed on. Adding a race this way changes no charlotte_*.bytes and no city code, so
    /// the baked city tiles stay valid (their fingerprint covers both).
    ///
    /// <see cref="Register"/> hands the routes to the parsed CityMap, so CityMode, the free-roam start
    /// gates and the editor's checks find them by id like the export's own. It refuses every route when
    /// the map's graph hash is not the file's: a re-export that moved the edges cannot send a race down
    /// the wrong road (re-run emit_tracks.mjs on the new graph). The menu reads the same file through
    /// <see cref="TryMenu"/> (TrackCatalog.EnsureRoute), never the city.
    /// </summary>
    public static class CityTracks
    {
        public const string Resource = "charlotte_tracks";   // charlotte_*: MAIN parks it by EditionParking's rule

        [System.Serializable]
        public class RouteData
        {
            public string id = "", name = "";
            public int loop, oneway;
            public float roadWidth, speed, lengthM, startM, finishM;
            public float[] pts;
            public int[] edges, dirs;
        }

        [System.Serializable]
        class FileData
        {
            public string attribution = "", graph = "";
            public RouteData[] routes;
        }

        static bool loaded;
        static FileData data;
        static uint fileHash;

        static FileData Data()
        {
            if (loaded) return data;
            loaded = true;
            var ta = Resources.Load<TextAsset>(Resource);
            if (ta == null) return null;
            try { data = JsonUtility.FromJson<FileData>(ta.text); }
            catch (System.Exception e) { Debug.LogError("[CityTracks] " + Resource + ".json: " + e.Message); data = null; }
            if (data != null && !uint.TryParse(data.graph, System.Globalization.NumberStyles.HexNumber, null, out fileHash))
            {
                Debug.LogError("[CityTracks] " + Resource + ".json carries no graph hash: none of its routes is used");
                data = null;
            }
            return data;
        }

        /// <summary>Is <paramref name="routeId"/> one of this file's routes?</summary>
        public static bool Has(string routeId) => Find(routeId) != null;

        public static RouteData Find(string routeId)
        {
            var d = Data();
            if (d == null || d.routes == null || string.IsNullOrEmpty(routeId)) return null;
            foreach (var r in d.routes) if (r != null && r.id == routeId) return r;
            return null;
        }

        /// <summary>The menu's facts for a route of this file: what charlotte_routes.json gives the
        /// export's own (TrackCatalog.EnsureRoute).</summary>
        public static bool TryMenu(string routeId, out RouteData route, out string attribution)
        {
            route = Find(routeId);
            attribution = data != null ? data.attribution ?? "" : "";
            return route != null && route.pts != null && route.pts.Length >= 4;
        }

        static uint registeredOn;
        static CityMap registeredMap;

        /// <summary>
        /// Hand this file's routes to <paramref name="map"/> (once per map): appended to map.routes as
        /// CityMap.Route, each checked edge by edge (every edge exists, each one ends where the next
        /// begins, a loop closes). Nothing is added when the graph is not the one they were routed on.
        /// </summary>
        public static int Register(CityMap map)
        {
            if (map == null || map.edges == null) return 0;
            if (ReferenceEquals(map, registeredMap)) return 0;
            registeredMap = map;
            var d = Data();
            if (d == null || d.routes == null || d.routes.Length == 0) return 0;
            if (map.graphHash != fileHash)
            {
                Debug.LogError($"[CityTracks] {Resource}.json was routed on graph {fileHash:x8}, this city is {map.graphHash:x8}: " +
                               "its races are left out (re-run tools/city/tracks/emit_tracks.mjs on this graph)");
                return 0;
            }
            var list = new List<CityMap.Route>(map.routes ?? new CityMap.Route[0]);
            int added = 0;
            foreach (var r in d.routes)
            {
                if (r == null || string.IsNullOrEmpty(r.id) || map.RouteById(r.id) != null) continue;
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
            if (r.edges == null || r.dirs == null || r.edges.Length == 0 || r.edges.Length != r.dirs.Length) return "no chain";
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
