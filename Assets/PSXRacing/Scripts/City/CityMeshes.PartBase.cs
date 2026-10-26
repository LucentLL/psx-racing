using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    public static partial class CityMeshes
    {
        // ------------------------------------------------------------------
        //  A TIER STANDS ON WHAT IS DRAWN UNDER IT (2026-10-26; the owner on
        //  South Tryon Street: "I see floating buildings. Is that
        //  intentional?"). A building:part with a floor (min_height, or the
        //  roof of the outline it rises from) is drawn from that floor up,
        //  over its outline and its lower parts. Each of those is cut clear of
        //  the drawn pavement ON ITS OWN (FitFootprint) - a podium can be cut
        //  back or left out while the tier on it, set back from the street,
        //  is not - and the tier then hung over air with its soffit showing.
        //
        //  Now every wall of a floored part asks what of ITS OWN building is
        //  drawn under its foot (the outline and the lower parts, each on the
        //  plan the tile really draws it on): where that does not reach the
        //  part's floor, the wall runs on down to the top of what is drawn
        //  there - the ground where nothing is - so it closes the gap without
        //  doubling a lower tier's face. The part's own plan was already cut
        //  clear of the pavement at the ground, so the wall stands where a car
        //  cannot reach. Walls over a drawn podium keep their floor.
        //  The same holds where OpenStreetMap maps nothing under a tier (45
        //  parts uptown: blocks of 5-78 m of wall hung 5-59 m up), except a
        //  slab no taller than CanopyM (a canopy, a marquee, a one-storey
        //  bridge piece) or a crown piece CrownPieceM up: those stay as mapped.
        //  Census: CityFloatProbe (city_float.txt).
        // ------------------------------------------------------------------

        /// <summary>0: a floored part floats as OSM's tiers say (the old
        /// draw); 1: its walls run down where its building is mapped under
        /// it but not DRAWN (cut back off the pavement, or left out); 2 (the
        /// game): also where OSM maps nothing under it, but canopies and
        /// crown pieces.</summary>
        public static int PartFillDown = 2;
        /// <summary>A tier with walls no taller than this over air is a
        /// canopy, a marquee or a cornice - mode 2 leaves it hanging.</summary>
        const float CanopyM = 4f;
        /// <summary>A floor this high over air is a piece of a crown (an open
        /// frame's slabs, a ring) - mode 2 leaves it hanging.</summary>
        const float CrownPieceM = 100f;
        /// <summary>How far inside a wall its foot is probed (metres).</summary>
        const float HangProbeM = 0.3f;

        /// <summary>The plan each footprint of a floored part's building is
        /// drawn on (FitFootprint's cut; null where it is left out), filled
        /// per tile build (the pavement tables are the tile's).</summary>
        static readonly Dictionary<int, Vector2[]> stackPlans = new Dictionary<int, Vector2[]>();
        static CityMap stackIndexMap;
        static Dictionary<int, List<int>> partsOfOutline;
        static readonly List<Vector2> stackPlanScratch = new List<Vector2>(32);
        static readonly List<int> stackScratch = new List<int>(16);
        /// <summary>Per wall of the part being drawn: -1 its foot stands on
        /// its building, else the height above the ground it runs down to
        /// (<see cref="PartHangs"/>). Read by EmitFootprint's wall loop.</summary>
        static readonly List<float> hangScratch = new List<float>(32);

        /// <summary>What the editor's float census reads (CityFloatProbe):
        /// one record per footprint a tile build drew or left out. Null in
        /// the game.</summary>
        public static List<FootRec> FootAudit;
        /// <summary>kind 0 drawn as a prism (plan = its drawn plan, baseY the
        /// floor its walls start from, topY its roof); 1 left out by the
        /// pavement cut; 2 a pack model stands there; 3 a parking deck; 4 a
        /// landmark hero draws it. filled: per wall of plan, run down to the
        /// ground (null: none).</summary>
        public struct FootRec
        {
            public int fi; public byte kind;
            public Vector2[] plan;
            public float ground, baseY, topY;
            public bool[] filled;
        }

        static void AuditFoot(int fi, byte kind, List<Vector2> plan, float ground, float baseY, float topY, bool filled)
        {
            if (FootAudit == null || skylineBuild) return;
            FootAudit.Add(new FootRec
            {
                fi = fi, kind = kind, plan = plan != null ? plan.ToArray() : null,
                ground = ground, baseY = baseY, topY = topY, filled = filled ? hangScratch.ConvertAll(x => x >= 0f).ToArray() : null,
            });
        }

        /// <summary>The footprints of part <paramref name="fi"/>'s building
        /// that stand under it: its outline (unless its parts draw it) and the
        /// outline's parts that start lower. Into <see cref="stackScratch"/>.</summary>
        static void StackUnder(CityMap map, int fi)
        {
            stackScratch.Clear();
            var fs = map.footprints;
            var f = fs[fi];
            if (f.outline < 0 || f.outline >= fs.Length) return;
            if (stackIndexMap != map)
            {
                stackIndexMap = map;
                partsOfOutline = new Dictionary<int, List<int>>();
                for (int i = 0; i < fs.Length; i++)
                {
                    if (!fs[i].part || fs[i].outline < 0) continue;
                    if (!partsOfOutline.TryGetValue(fs[i].outline, out var l)) partsOfOutline[fs[i].outline] = l = new List<int>(4);
                    l.Add(i);
                }
            }
            if (!fs[f.outline].hidden) stackScratch.Add(f.outline);
            if (partsOfOutline.TryGetValue(f.outline, out var ps))
                foreach (int j in ps)
                    if (j != fi && !fs[j].hidden && fs[j].minH < f.minH - 0.05f) stackScratch.Add(j);
        }

        /// <summary>Cut, the way EmitFootprint cuts it, the plan of every
        /// footprint under floored part <paramref name="fi"/>. Called BEFORE
        /// the part's own cut, so the pavement tables the part's walls read
        /// afterwards are its own.</summary>
        static void PrimeStack(CityMap map, Trims trims, int fi)
        {
            StackUnder(map, fi);
            if (heroMap != map) IndexHeroes(map);
            var fs = map.footprints;
            foreach (int si in stackScratch)
            {
                if (stackPlans.ContainsKey(si)) continue;
                var s = fs[si];
                Vector2[] plan = s.pts;   // drawn whole: a pack model, a deck, a hero's own, a house's box
                if (s.propKind == 0 && !s.gable && CityDecks.ForFootprint(si) == null && !heroSkip.Contains(si))
                {
                    var gs = s.part && s.outline >= 0 && s.outline < fs.Length ? fs[s.outline] : s;
                    float g = float.MaxValue;
                    foreach (var p in s.pts) g = Mathf.Min(g, CityElevation.GroundY(map, p.x, p.y));
                    if (gs != s) foreach (var p in gs.pts) g = Mathf.Min(g, CityElevation.GroundY(map, p.x, p.y));
                    stackPlanScratch.Clear();
                    stackPlanScratch.AddRange(s.pts);
                    int cut = FitFootprint(map, trims, stackPlanScratch, g - BuildingSink, g + s.h);
                    plan = cut < 0 ? null : cut == 0 ? s.pts : stackPlanScratch.ToArray();
                }
                stackPlans[si] = plan;
            }
        }

        /// <summary>How high above the floor the stack (as drawn, or as OSM
        /// maps it) fills the column at <paramref name="q"/> without a gap,
        /// stopping once it reaches <paramref name="floor"/>. The stack is the
        /// last <see cref="PrimeStack"/>'s.</summary>
        static float StackTop(CityMap map, Vector2 q, float floor, bool drawn)
        {
            var fs = map.footprints;
            float cur = 0f;
            for (int pass = 0; pass <= stackScratch.Count; pass++)
            {
                bool grew = false;
                foreach (int j in stackScratch)
                {
                    var s = fs[j];
                    float b = s.part ? s.minH : 0f;
                    if (b > cur + 0.6f || s.h <= cur) continue;
                    var plan = drawn ? stackPlans[j] : s.pts;
                    if (plan == null || !CityMap.PointInPoly(plan, q)) continue;
                    cur = s.h; grew = true;
                    if (cur >= floor - 0.6f) return cur;
                }
                if (!grew) break;
            }
            return cur;
        }

        /// <summary>Fill <see cref="hangScratch"/>, one per wall of the
        /// floored part's drawn plan <paramref name="poly"/>: -1 where the
        /// wall's foot stands on its building, else how far above the ground
        /// (0: the ground itself) the drawn building under it reaches - the
        /// wall runs down to there, so it never doubles a lower tier's face.
        /// True when any wall hangs. Needs <see cref="PrimeStack"/> for the
        /// same part first.</summary>
        static bool PartHangs(CityMap map, int fi, List<Vector2> poly)
        {
            hangScratch.Clear();
            var f = map.footprints[fi];
            // mode 2 leaves OSM's canopies and crown pieces as mapped
            float wallH = f.h - (f.roof != 0 ? f.roofH : 0f) - f.minH;
            bool osmGapsToo = PartFillDown >= 2 && wallH > CanopyM && f.minH < CrownPieceM;
            bool any = false;
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i], c = poly[(i + 1) % n], d = c - a;
                float len = d.magnitude;
                float foot = -1f;
                if (len >= 0.2f)
                {
                    var inward = new Vector2(-d.y, d.x) / len;   // a CCW plan: the left is in
                    int k = Mathf.Clamp(Mathf.CeilToInt(len / 4f), 2, 16);
                    for (int s = 0; s < k; s++)
                    {
                        var q = Vector2.Lerp(a, c, (s + 0.5f) / k) + inward * HangProbeM;
                        float top = StackTop(map, q, f.minH, true);
                        if (top >= f.minH - 0.6f) continue;
                        if (!osmGapsToo && StackTop(map, q, f.minH, false) < f.minH - 0.6f) continue;   // OSM's own overhang
                        foot = foot < 0f ? top : Mathf.Min(foot, top);
                    }
                }
                hangScratch.Add(foot);
                any |= foot >= 0f;
            }
            return any;
        }

        /// <summary>Where wall <paramref name="i"/> of the part just tested
        /// by <see cref="PartHangs"/> starts: its floor, or down at what is
        /// drawn under it, or the ground (<paramref name="y0"/>).</summary>
        static float HangFoot(int i, float y0, float floorY, float wallY0)
        {
            float foot = i < hangScratch.Count ? hangScratch[i] : -1f;
            return foot < 0f ? wallY0 : foot <= 0.05f ? y0 : floorY + foot;
        }
    }
}
