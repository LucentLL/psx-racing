using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// DECK FACTS (exit 3A, 2026-10-05): every edge in a box with its OSM
    /// facts (bridge, layer, link) next to what the solve made of it - how
    /// many stations are structure, how far it stands over the road ground -
    /// and the grade separations in the box. Headless:
    /// -executeMethod PSXRacing.EditorTools.CityDeckFacts.Run with
    /// PSX_DECK_BOX=x0,z0,x1,z1 (game metres); writes deck_facts.txt.
    /// </summary>
    public static class CityDeckFacts
    {
        public static void Run()
        {
            var map = CityMap.Get();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var f = (System.Environment.GetEnvironmentVariable("PSX_DECK_BOX") ?? "-1500,4500,-700,5300").Split(',');
            float x0 = float.Parse(f[0], inv), z0 = float.Parse(f[1], inv), x1 = float.Parse(f[2], inv), z1 = float.Parse(f[3], inv);
            bool In(Vector2 p) => p.x >= x0 && p.x <= x1 && p.y >= z0 && p.y <= z1;
            var sb = new StringBuilder();
            foreach (var e in map.edges)
            {
                if (e.stS == null) continue;
                bool any = false;
                foreach (var p in e.pts) if (In(p)) { any = true; break; }
                if (!any) continue;
                int nEl = 0, nSeat = 0; float maxUp = -99f, minUp = 99f;
                var st = new StringBuilder();
                for (int i = 0; i < e.stS.Length; i++)
                {
                    var p = e.PointAt(e.stS[i]);
                    float up = e.stY[i] - CityElevation.RoadBaseY(p.x, p.y);
                    if (e.stElev[i]) nEl++;
                    if (e.SeatedAt(i)) nSeat++;
                    maxUp = Mathf.Max(maxUp, up); minUp = Mathf.Min(minUp, up);
                    st.Append(string.Format(inv, " {0:0}{1}{2}", up, e.stElev[i] ? "E" : "", e.SeatedAt(i) ? "s" : ""));
                }
                sb.AppendLine(string.Format(inv, "e{0} w{1} '{2}' c{3}{4}{5} L{6} sid{7} len{8:0} a{9} b{10} ({11:0},{12:0})->({13:0},{14:0}) st{15} el{16} seat{17} up[{18:0.0},{19:0.0}] |{20}",
                    e.index, e.wayId, e.name, e.cls, e.link ? " link" : "", e.bridge ? " BRIDGE" : "", e.layer, e.structId, e.length, e.a, e.b,
                    e.pts[0].x, e.pts[0].y, e.pts[e.pts.Length - 1].x, e.pts[e.pts.Length - 1].y, e.stS.Length, nEl, nSeat, minUp, maxUp, st));
            }
            // PSX_DECK_EDGES=372,1326: those edges station by station, the
            // road's height / the raw 3DEP ground / the smoothed road ground
            foreach (var t in (System.Environment.GetEnvironmentVariable("PSX_DECK_EDGES") ?? "").Split(','))
            {
                if (!int.TryParse(t, out int ei) || ei < 0 || ei >= map.edges.Length || map.edges[ei].stS == null) continue;
                var e = map.edges[ei];
                var st = new StringBuilder();
                for (int i = 0; i < e.stS.Length; i++)
                {
                    var p = e.PointAt(e.stS[i]);
                    st.Append(string.Format(inv, " {0:0}:{1:0.0}/{2:0.0}/{3:0.0}", e.stS[i], e.stY[i], CityElevation.BaseY(p.x, p.y), CityElevation.RoadBaseY(p.x, p.y)));
                }
                sb.AppendLine(string.Format(inv, "ABS e{0} '{1}' s:y/dem/road |{2}", ei, e.name, st));
            }
            var why = CityElevation.TrenchWhy; var on = CityElevation.EnforcedCrossings; var tr = CityElevation.TrenchedCrossings;
            for (int ci = 0; ci < map.crossings.Length; ci++)
            {
                var c = map.crossings[ci];
                if (!In(c.at)) continue;
                CityElevation.ProjectOn(map.edges[c.over], c.at, out float so);
                CityElevation.ProjectOn(map.edges[c.under], c.at, out float su);
                sb.AppendLine(string.Format(inv, "X{7} over e{0} '{1}' y{8:0.0} under e{2} '{3}' y{9:0.0} at ({4:0},{5:0}) forced {6} on {10} dug {11} why {12}",
                    c.over, map.edges[c.over].name, c.under, map.edges[c.under].name, c.at.x, c.at.y, c.forced, ci,
                    map.edges[c.over].YAt(so), map.edges[c.under].YAt(su),
                    on != null && ci < on.Length && on[ci], tr != null && ci < tr.Length && tr[ci], why != null && ci < why.Length ? why[ci] : "-"));
            }
            // THE CENSUS, city-wide: roads OSM does not call bridges standing on
            // structure more than ElevMarginM over their ground (decks from
            // heights alone), freeway cuts filled back up (a dug crossing whose
            // freeway ends above its ground), and steep freeway steps
            int raisedEdges = 0, raisedSt = 0, filledCuts = 0, steep = 0; float raisedM = 0f, steepMax = 0f;
            foreach (var e in map.edges)
            {
                if (e.stS == null) continue;
                if (!e.bridge && (e.cls < 5 || e.link))
                {
                    int k = 0;
                    for (int i = 0; i < e.stS.Length; i++)
                    {
                        var p = e.PointAt(e.stS[i]);
                        if (e.stElev[i] && e.stY[i] > CityElevation.RoadBaseY(p.x, p.y) + CityElevation.ElevMarginM) k++;
                    }
                    if (k > 0) { raisedEdges++; raisedSt += k; raisedM += e.length * k / e.stS.Length; }
                }
                if (e.cls >= 5 && !e.link)
                    for (int i = 1; i < e.stS.Length; i++)
                    {
                        float g = Mathf.Abs(e.stY[i] - e.stY[i - 1]) / Mathf.Max(1f, e.stS[i] - e.stS[i - 1]);
                        if (g > 0.08f) { steep++; steepMax = Mathf.Max(steepMax, g); var q = e.PointAt(e.stS[i]); sb.AppendLine(string.Format(inv, "STEEP e{0} '{1}' s{2:0}/{3:0} ({4:0},{5:0}) {6:0}% el{7} n{8}/{9}", e.index, e.name, e.stS[i], e.length, q.x, q.y, 100f * g, e.stElev[i] ? 1 : 0, e.a, e.b)); }
                    }
            }
            var dugX = CityElevation.TrenchedCrossings;
            for (int ci = 0; dugX != null && ci < dugX.Length; ci++)
            {
                if (!dugX[ci]) continue;
                var c = map.crossings[ci]; var u = map.edges[c.under];
                CityElevation.ProjectOn(u, c.at, out float su);
                if (u.YAt(su) > CityElevation.RoadBaseY(c.at.x, c.at.y) - 1f) filledCuts++;
            }
            string census = string.Format(inv, "CENSUS underpass={0}: non-bridge roads on structure > {1:0.0} m over the ground {2} edges ({3} stations, {4:0} m); dug freeway cuts filled back to the ground {5}; freeway station steps > 8% {6} (worst {7:0.0}%); underpasses {8} (deepest {9:0.0} m)",
                CityElevation.UnderpassOn ? "on" : "off", CityElevation.ElevMarginM, raisedEdges, raisedSt, raisedM, filledCuts, steep, 100f * steepMax,
                CityElevation.UnderpassCrossings, CityElevation.UnderpassDeepestM);
            sb.AppendLine(census);
            Debug.Log("[DeckFacts] " + census);
            System.IO.File.WriteAllText("deck_facts.txt", sb.ToString());
            Debug.Log($"[DeckFacts] wrote deck_facts.txt {sb.Length}; underpasses {CityElevation.UnderpassCrossings} (deepest {CityElevation.UnderpassDeepestM:0.0} m)");
        }
    }
}
