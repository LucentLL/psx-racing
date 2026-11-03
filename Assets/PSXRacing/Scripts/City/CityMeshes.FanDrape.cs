using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    public static partial class CityMeshes
    {
        /// <summary>
        /// A RIBBON LYING ON A JUNCTION FAN TAKES THE FAN'S HEIGHT (Five Points
        /// round 3, 2026-11-03).
        ///
        /// A ribbon's cross-section is level: both edge vertices stand at the
        /// height of the road's own line. Where a short arm's ribbon reaches
        /// INTO a fan whose surface slopes across it, its edge stands over the
        /// fan at the wrong height and the car meets a step along that edge.
        /// Five Points' W 5th link (e9644/e9645, 10 m between Beatties Ford's
        /// two carriageways): put back on its centre line by the export (round
        /// 2), its south half lay 1-4 m into the fan of n10969, whose surface
        /// falls 3% toward West Trade's southbound mouth; the ribbon's edge stood
        /// 5-8 cm over it on the driving line (T2 BF SB -> Trade SB, T4, L5).
        /// <see cref="BuriedEndOffset"/> already does this for an arm whose
        /// whole mouth lies under the fan; this is the rule for a section with
        /// an edge vertex INSIDE the fan of the junction the ribbon belongs to
        /// (its own end nodes, or the far end of a short arm through a mitre).
        /// A level ribbon cannot follow a sloped fan under it (tilting it moved
        /// the step to the fan's chord), so that side is CUT BACK along the
        /// section line to where it enters the fan, at the fan's height there:
        /// the fan alone paves its own area and the two meet along the fan's
        /// edge at one height. A section wholly inside takes the fan's height
        /// at both vertices. A vertex on the fan's ring (the arm's own mouth)
        /// is left alone, so the fan's corners and the ribbons that make them
        /// never move; the rule is a function of position alone, so two
        /// ribbons mitred at a section both cut it the same. Not on structure, not
        /// more than <see cref="FanDrapeMaxM"/> off (another level). Switch
        /// PSX_CITY_FAN_DRAPE=0: the level cross-section as before.
        /// </summary>
        public static bool FanDrapeOn = System.Environment.GetEnvironmentVariable("PSX_CITY_FAN_DRAPE") != "0";
        /// <summary>A vertex this far inside the fan's ring (or more) is draped.</summary>
        const float FanDrapeInM = 0.10f;
        /// <summary>No drape past this height difference: another level.</summary>
        const float FanDrapeMaxM = 0.25f;
        /// <summary>A short arm through a mitre reaches the fan at its far end.</summary>
        const float FanDrapeArmM = 30f;
        /// <summary>For the probe: vertices draped city-wide since the reset, and the largest move.</summary>
        public static int FanDrapeN; public static float FanDrapeMaxMoved;

        static readonly List<int> drapeNodes = new List<int>(4);

        static void AddDrapeNode(int n) { if (!drapeNodes.Contains(n)) drapeNodes.Add(n); }

        static void DrapeNodesOf(CityMap map, Trims trims, CityMap.Edge e, int node)
        {
            if (trims.patch[node]) { AddDrapeNode(node); return; }
            var arms = map.nodeEdges[node];
            if (arms == null || arms.Count != 2) return;
            foreach (int oi in arms)
            {
                if (oi == e.index) continue;
                var o = map.edges[oi];
                if (o.a == o.b || o.length > FanDrapeArmM) continue;
                int far = o.a == node ? o.b : o.a;
                if (trims.patch[far]) AddDrapeNode(far);
            }
        }

        /// <summary>The fan's height under a plan point at least
        /// <see cref="FanDrapeInM"/> inside its ring; false outside it.</summary>
        static bool InsideFanY(CityMap map, Trims trims, int node, Vector2 q, out float y)
        {
            y = 0f;
            var (ring, tris) = FanRing(map, trims, node);
            if (ring.Count < 3 || tris.Count < 3) return false;
            bool inside = false;
            float dMin = float.MaxValue;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                Vector2 a = new Vector2(ring[j].pos.x, ring[j].pos.z), b = new Vector2(ring[i].pos.x, ring[i].pos.z);
                if ((b.y > q.y) != (a.y > q.y) && q.x < (a.x - b.x) * (q.y - b.y) / (a.y - b.y) + b.x) inside = !inside;
                var ab = a - b; float l2 = ab.sqrMagnitude;
                float t = l2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(q - b, ab) / l2) : 0f;
                dMin = Mathf.Min(dMin, Vector2.Distance(q, b + ab * t));
            }
            if (!inside || dMin < FanDrapeInM) return false;
            if (!FanY(map, trims, node, ring, tris, q, out y)) return false;
            y -= FanProudM;
            return true;
        }

        /// <summary>Where the segment from outside point w to inside point v
        /// last crosses the fan's ring (the parameter from w), or -1.</summary>
        static float RingEntry(List<FanCorner> ring, Vector2 w, Vector2 v)
        {
            float best = -1f; var d = v - w;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                Vector2 a = new Vector2(ring[j].pos.x, ring[j].pos.z), b = new Vector2(ring[i].pos.x, ring[i].pos.z), e = b - a;
                float den = d.x * e.y - d.y * e.x;
                if (Mathf.Abs(den) < 1e-8f) continue;
                var aw = a - w;
                float t = (aw.x * e.y - aw.y * e.x) / den, u = (aw.x * d.y - aw.y * d.x) / den;
                if (t >= 0f && t <= 1f && u >= 0f && u <= 1f && t > best) best = t;
            }
            return best;
        }

        static void DrapeOnFans(CityMap map, Trims trims, CityMap.Edge e, List<Section> list)
        {
            int n = list.Count;
            if (!FanDrapeOn || n < 2 || e.a == e.b || e.tunnel || e.bridge) return;
            drapeNodes.Clear();
            DrapeNodesOf(map, trims, e, e.a);
            DrapeNodesOf(map, trims, e, e.b);
            if (drapeNodes.Count == 0) return;
            for (int k = 0; k < n; k++)
            {
                var sec = list[k];
                if (sec.elev || sec.collapsed) continue;
                bool moved = false;
                foreach (int nd in drapeNodes)
                {
                    var L2 = new Vector2(sec.L.x, sec.L.z); var R2 = new Vector2(sec.R.x, sec.R.z);
                    if ((L2 - map.nodes[nd]).sqrMagnitude > 50f * 50f && (R2 - map.nodes[nd]).sqrMagnitude > 50f * 50f) continue;
                    bool inL = InsideFanY(map, trims, nd, L2, out float yL), inR = InsideFanY(map, trims, nd, R2, out float yR);
                    if (!inL && !inR) continue;
                    var (ring, tris) = FanRing(map, trims, nd);
                    if (inL && inR)
                    {
                        // the whole section lies on the fan: both vertices on it
                        if (Mathf.Abs(yL - sec.L.y) > FanDrapeMaxM || Mathf.Abs(yR - sec.R.y) > FanDrapeMaxM) continue;
                        Note(yL - sec.L.y); Note(yR - sec.R.y);
                        sec.L.y = yL; sec.R.y = yR; moved = true; break;
                    }
                    // half of it does: that side is cut back to where it enters
                    // the fan (the fan paves the rest) and stands on the fan's
                    // height there, so the two meet along the fan's own edge
                    var vin = inL ? L2 : R2; var vout = inL ? R2 : L2;
                    float t = RingEntry(ring, vout, vin);
                    if (t < 0f) continue;
                    var c = Vector2.Lerp(vout, vin, t);
                    if (!FanY(map, trims, nd, ring, tris, c, out float yc)) continue;
                    yc -= FanProudM;
                    var old = inL ? sec.L : sec.R;
                    if (Mathf.Abs(yc - old.y) > FanDrapeMaxM) continue;
                    Note(yc - old.y);
                    var nv = new Vector3(c.x, yc, c.y);
                    if (inL) { sec.L = nv; sec.sqL = true; } else { sec.R = nv; sec.sqR = true; }
                    moved = true; break;
                }
                if (moved) list[k] = sec;
            }
        }

        static void Note(float d) { if (Mathf.Abs(d) < 1e-4f) return; FanDrapeN++; FanDrapeMaxMoved = Mathf.Max(FanDrapeMaxMoved, Mathf.Abs(d)); }
    }
}
