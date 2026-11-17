using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// A CITY CHECKPOINT CHAIN (owner 2026-11-16 ~21:00: "the Midnight Club approach is the way with checkpoints and
    /// allow players to pathfind"; no barricades anywhere). One file per route id,
    /// <c>Resources/CityCheckpoints/&lt;routeId&gt;.json</c>, written by tools/city/tracks/checkpoints.mjs (the gates,
    /// their wide spans and the audit that no leg can be cut). A city venue whose route has a file races as a
    /// CHECKPOINT race (<see cref="CheckpointRace"/>): an ordered chain of gates, any road between them. A file routed
    /// on another graph is refused, and that venue races its fixed line as before (one log line).
    /// The menu may read the file (x, z, kind) without parsing the city.
    ///
    /// SWITCHES: PSX_CP_RACES (master; default <see cref="RacesDefault"/>), PSX_CP_VENUES (dev: a comma list of venue
    /// ids that may run checkpoint mode; empty = every venue with a file).
    /// </summary>
    public static class CheckpointFile
    {
        public const string Folder = "CityCheckpoints";

        [System.Serializable]
        public class Span
        {
            public int e;
            public int d = 1;
            public float s;
            public float l;
        }

        [System.Serializable]
        public class Gate
        {
            public string kind;          // line | turn | split | apex | finish
            public float routeM;
            public int e;
            public float s;
            public int d = 1;
            public float x, y, z;
            public float hx, hz;
            public float depth = 6f;
            public Span[] spans;
            public float legM, optM, metM;
            public string name;
        }

        [System.Serializable]
        public class Chain
        {
            public string dir;           // fwd | rev
            public int loop;
            public Gate[] cps;
        }

        [System.Serializable]
        public class Data
        {
            public int version;
            public string graph;
            public string route;
            public string candidate;
            public string generator;
            public string cost;          // fwy | city
            public int loop;
            public float lengthM, startM, finishM;
            public Chain[] chains;
            [System.NonSerialized] public uint graphHash;
        }

        /// <summary>The master switch's default: ON (2026-11-17, after the watched I485West races: by day, at night in
        /// rain, with LIGHT traffic and the 2-lap run). Only venues whose route has a file convert (stage 1:
        /// i485west); every other city venue races its fixed line. PSX_CP_RACES=0: every city race fixed-line.</summary>
        public const bool RacesDefault = true;

        public static bool RacesOn
        {
            get
            {
                string v = System.Environment.GetEnvironmentVariable("PSX_CP_RACES");
                if (v == "0") return false;
                if (v == "1") return true;
                return RacesDefault;
            }
        }

        /// <summary>PSX_CP_VENUES: empty = every venue with a file.</summary>
        public static bool VenueAllowed(string venueId)
        {
            string v = System.Environment.GetEnvironmentVariable("PSX_CP_VENUES");
            if (string.IsNullOrEmpty(v)) return true;
            foreach (var part in v.Split(','))
                if (string.Equals(part.Trim(), venueId, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>A sub-switch of stage 1: unset or anything but "0" = on.</summary>
        public static bool On(string name) => System.Environment.GetEnvironmentVariable(name) != "0";

        static readonly Dictionary<string, Data> cache = new Dictionary<string, Data>();

        /// <summary>The file for a route id, parsed once (null: no file, or it does not parse).</summary>
        public static Data Load(string routeId)
        {
            if (string.IsNullOrEmpty(routeId)) return null;
            if (cache.TryGetValue(routeId, out var d)) return d;
            d = null;
            var ta = Resources.Load<TextAsset>(Folder + "/" + routeId);
            if (ta != null)
            {
                try { d = JsonUtility.FromJson<Data>(ta.text); }
                catch (System.Exception e) { Debug.LogError("[Checkpoints] " + Folder + "/" + routeId + ".json: " + e.Message); d = null; }
                uint gh = 0;
                string why = d == null ? "does not parse"
                    : d.chains == null || d.chains.Length == 0 ? "has no chain"
                    : !uint.TryParse(d.graph, System.Globalization.NumberStyles.HexNumber, null, out gh) ? "carries no graph hash"
                    : null;
                if (why == null)
                    foreach (var c in d.chains)
                        if (c.cps == null || c.cps.Length < 2) { why = "has a chain with fewer than two gates"; break; }
                if (why != null) { Debug.LogError("[Checkpoints] " + Folder + "/" + routeId + ".json " + why + ": the venue races its fixed line"); d = null; }
                else d.graphHash = gh;
            }
            cache[routeId] = d;
            return d;
        }

        /// <summary>The race card's facts for a venue that races as a checkpoint race ("  ·  15 CHECKPOINTS  ·  ANY
        /// ROUTE"), "" otherwise. The menu's own test, the same as TryBegin's file test (no city parse).</summary>
        public static string CardFacts(TrackCatalog.TrackDef t)
        {
            if (t == null || !t.IsCityRace || t.IsDeckRun || t.IsDragEvent || !RacesOn || !VenueAllowed(t.id)) return "";
            var ch = ChainFor(Load(t.cityRoute), t.Reversed);
            if (ch == null) return "";
            int n = ch.loop != 0 ? ch.cps.Length : ch.cps.Length - 1;
            return "  \u00b7  " + n + " CHECKPOINTS  \u00b7  ANY ROUTE";
        }

        /// <summary>Is there a usable file for this route (no city parse: the menu may ask)?</summary>
        public static bool Has(string routeId) => Load(routeId) != null;

        /// <summary>The chain for the raced direction ("rev" for a reverse twin), or null.</summary>
        public static Chain ChainFor(Data d, bool reversed)
        {
            if (d == null || d.chains == null) return null;
            string want = reversed ? "rev" : "fwd";
            foreach (var c in d.chains) if (c != null && c.dir == want) return c;
            return null;
        }

        /// <summary>The file matches this city's road graph (anchors are keyed by edge index and arc).</summary>
        public static bool Matches(Data d, City.CityMap map, out string why)
        {
            why = null;
            if (d == null || map == null) { why = "no file or no map"; return false; }
            if (d.graphHash != map.graphHash)
            {
                why = $"routed on graph {d.graphHash:x8}, this city is {map.graphHash:x8}";
                return false;
            }
            foreach (var c in d.chains)
                foreach (var g in c.cps)
                {
                    if (g.e < 0 || g.e >= map.edges.Length) { why = "a gate on edge " + g.e + " which this city does not have"; return false; }
                    if (g.spans != null)
                        foreach (var s in g.spans)
                            if (s.e < 0 || s.e >= map.edges.Length) { why = "a span on edge " + s.e + " which this city does not have"; return false; }
                }
            return true;
        }
    }
}
