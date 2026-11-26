using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  c7roads round 4 (owner 11-26 on spot 80, "What are these roads?"): the median crossovers the map draws
    //  as their own one-way links between the two carriageways of a divided road (a U-turn / left-turn bay and
    //  its turn across the median). R0 (LineModel.BayShift + the merge zones) carries the bay as its host's
    //  lane; this file makes the turn across end in ONE paved surface with the far carriageway.
    //
    //  A1, THE CROSSING: the far node (where the link meets the other carriageway) was a mitred node - the
    //  link's ribbon lay over the far carriageway's lanes (spot 80: the island gate's OVERLAY 0 -> 79). It is
    //  forced to a junction fan (ComputeTrims' forceFan, the same path the mitre-fan rule uses), so every arm
    //  is trimmed back and the fan's one plate covers the meeting.
    //
    //  A CROSSOVER: a one-way link way (not a motorway link) of at most CrossoverMaxM, whose start and end
    //  nodes each lie on a non-link road of one name, the two flowing in opposite directions there (the two
    //  carriageways of one divided road). Its far node is forced to a fan when it is mitred, has three or more
    //  arms and is on no deck, tunnel or refuge.
    // ======================================================================
    public static partial class CityMeshes
    {
        public const float CrossoverMaxM = 250f, CrossoverOppCos = -0.5f;
        public static int CrossoverFans;
        public static readonly List<int> CrossoverFanNodes = new List<int>();

        static List<int> CrossoverFarNodes(CityMap map, Trims t)
        {
            CrossoverFanNodes.Clear();
            var ways = new Dictionary<uint, List<int>>();
            foreach (var e in map.edges)
                if (e.link && e.oneway && e.cls < 5 && e.a != e.b)
                {
                    if (!ways.TryGetValue(e.wayId, out var l)) ways[e.wayId] = l = new List<int>(2);
                    l.Add(e.index);
                }
            var deg = new Dictionary<int, int>();
            foreach (var kv in ways)
            {
                var es = kv.Value;
                float len = 0f; foreach (int i in es) len += map.edges[i].length;
                if (len > CrossoverMaxM) continue;
                deg.Clear();
                foreach (int i in es) { var e = map.edges[i]; deg[e.a] = deg.TryGetValue(e.a, out int c1) ? c1 + 1 : 1; deg[e.b] = deg.TryGetValue(e.b, out int c2) ? c2 + 1 : 1; }
                // a one-way way's pts run with the traffic: its start is an a end, its far end a b end
                int start = -1, far = -1;
                foreach (int i in es)
                {
                    var e = map.edges[i];
                    if (deg[e.a] == 1) start = e.a;
                    if (deg[e.b] == 1) far = e.b;
                }
                if (start < 0 || far < 0 || start == far) continue;
                if (!CarriagewayAt(map, start, es, null, out var hS, out var dS)) continue;
                if (!CarriagewayAt(map, far, es, hS.name, out var hF, out var dF)) continue;
                if (Vector2.Dot(dS, dF) > CrossoverOppCos) continue;           // not the opposite carriageway
                if (!t.mitre[far] || map.nodeEdges[far].Count < 3 || LineModel.IsRefugeNode(far)) continue;
                bool deck = false;
                foreach (int fi in map.nodeEdges[far]) deck |= map.edges[fi].bridge || map.edges[fi].tunnel;
                if (deck) continue;
                if (!CrossoverFanNodes.Contains(far)) CrossoverFanNodes.Add(far);
            }
            return CrossoverFanNodes;
        }

        /// <summary>A non-link named road at the node (of the given name when one is given) and its flow direction
        /// there (its pts run with the traffic when one-way).</summary>
        static bool CarriagewayAt(CityMap map, int node, List<int> skip, string name, out CityMap.Edge h, out Vector2 dir)
        {
            h = null; dir = default;
            foreach (int i in map.nodeEdges[node])
            {
                if (skip.Contains(i)) continue;
                var e = map.edges[i];
                if (e.link || string.IsNullOrEmpty(e.name) || !e.oneway) continue;
                if (name != null && e.name != name) continue;
                h = e;
                dir = e.a == node ? e.TangentAt(0f) : e.TangentAt(e.length);
                return true;
            }
            return false;
        }
    }
}
