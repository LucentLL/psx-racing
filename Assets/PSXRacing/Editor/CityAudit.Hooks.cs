using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE ROADS PASS'S REPORTS, AS HOOKS (2026-10-02, plan P0).
    ///
    /// The Charlotte roads pass adds seven audits, written by two lanes at the
    /// same time. None of them edits <see cref="Run"/>: each is one partial
    /// method, declared here and called by Run in this order, right after the
    /// LINE MODEL block (with the map, its trims and the buildings ready):
    ///
    ///   TwinReport      twin decks drawn as one structure (B1, lane B)
    ///   ProfileReport   vertical curves, trench decisions (B2, lane B)
    ///   PaintReport     line meaning, white and yellow (B3; lane A from M3)
    ///   MergeReport     merge zones, gores (B5; lane A from M5)
    ///   CoverageReport  holes, coplanar overlaps, overshoot (A1, lane A)
    ///   LotReport       parking lots (B10, lane B)
    ///   BulbReport      cul-de-sac bulbs (A17, lane A)
    ///
    /// A package implements its hook in its OWN partial file
    /// (Editor/CityAudit.Twin.cs: <c>static partial void TwinReport(CityMap map,
    /// CityMeshes.Trims trims) { ... }</c>). A hook nobody implements compiles
    /// away - the call and all - so until then the audit's output is exactly
    /// what it was. Write through <see cref="Line"/> / <see cref="Check"/>
    /// (a Check is a gate: it counts in "CITY AUDIT: N FAILURES"), and start
    /// with one line naming the report and its scope
    /// (<see cref="AuditScope.Describe"/>). The buildings every tile build
    /// needs are <see cref="AuditBuildings"/>. FROZEN (plan rule 4): these
    /// signatures, <see cref="AuditScope"/> and <see cref="ScopeFor"/> do not
    /// change; anything new is an overload.
    ///
    /// WHERE A REPORT RUNS (critic C7). City-wide only under
    /// PSX_AUDIT_FULL=1 (the release gates; tools\city-cycle.ps1 -Full);
    /// otherwise BOXED, so a package's AuditOnly stays minutes long. See
    /// <see cref="ScopeFor"/> for the switches; tools\city-cycle.ps1's header
    /// lists them too.
    /// </summary>
    public static partial class CityAudit
    {
        static partial void TwinReport(CityMap map, CityMeshes.Trims trims);
        static partial void ProfileReport(CityMap map, CityMeshes.Trims trims);
        static partial void PaintReport(CityMap map, CityMeshes.Trims trims);
        static partial void MergeReport(CityMap map, CityMeshes.Trims trims);
        static partial void CoverageReport(CityMap map, CityMeshes.Trims trims);
        static partial void LotReport(CityMap map, CityMeshes.Trims trims);
        static partial void BulbReport(CityMap map, CityMeshes.Trims trims);

        /// <summary>The buildings a tile build takes
        /// (<see cref="CityBuildings.Precompute"/>), computed once by Run
        /// before the hooks; null outside Run.</summary>
        static Dictionary<long, List<CityBuildings.B>> AuditBuildings;

        /// <summary>Run's hooks, in the documented order.</summary>
        static void RoadsPassReports(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings)
        {
            AuditBuildings = buildings;
            try
            {
                TwinReport(map, trims);
                ProfileReport(map, trims);
                PaintReport(map, trims);
                MergeReport(map, trims);
                CoverageReport(map, trims);
                LotReport(map, trims);
                BulbReport(map, trims);
            }
            finally { AuditBuildings = null; }
        }

        /// <summary>The default box a report runs on when no box is set and
        /// PSX_AUDIT_FULL is not 1: uptown inside the I-277 loop plus West 5th
        /// Street over I-77 (the owner's example, node 2069 at game
        /// (-3364, 5940)). Game metres, x east, z north (Rect y = z):
        /// 35.214-35.244 N, 80.862-80.829 W, 3.0 x 3.3 km.</summary>
        public static readonly Rect OwnerBox = Rect.MinMaxRect(-4000f, 3300f, -1000f, 6600f);

        /// <summary>
        /// Where one report runs and which tiers it counts. Read from the
        /// environment once per call (<see cref="ScopeFor"/>):
        ///
        ///   PSX_AUDIT_FULL=1             city-wide (tiers still apply)
        ///   PSX_&lt;NAME&gt;_BOX=x0,z0,x1,z1  this report's box (game metres)
        ///   PSX_AUDIT_BOX=x0,z0,x1,z1    every report's box
        ///   (neither)                    <see cref="OwnerBox"/>
        ///   PSX_&lt;NAME&gt;_TIER, PSX_AUDIT_TIER = "1", "1,2", "all" (default all)
        ///
        /// NAME is the report's: COVER, TWIN, PROFILE, PAINT, MERGE, LOT, BULB.
        /// A value that does not parse is reported in <see cref="Describe"/>
        /// and the default is used - never a silent city-wide run.
        /// </summary>
        public sealed class AuditScope
        {
            public readonly string Name;
            /// <summary>PSX_AUDIT_FULL=1: the whole city.</summary>
            public readonly bool Full;
            /// <summary>The box when not <see cref="Full"/>: x = world x,
            /// y = world z.</summary>
            public readonly Rect Box;
            /// <summary>Which variable the box came from, or "default".</summary>
            public readonly string BoxFrom;
            /// <summary>A switch that did not parse, or null.</summary>
            public readonly string Problem;
            readonly bool[] tiers = new bool[4];

            internal AuditScope(string name, bool full, Rect box, string boxFrom, bool[] tierMask, string problem)
            {
                Name = name; Full = full; Box = box; BoxFrom = boxFrom; Problem = problem;
                for (int t = 1; t <= 3; t++) tiers[t] = tierMask[t];
            }

            /// <summary>Does this report count tier t (1..3)?</summary>
            public bool HasTier(int tier) => tier >= 1 && tier <= 3 && tiers[tier];

            /// <summary>Is the plan point (world x, z) inside?</summary>
            public bool Contains(float x, float z) => Full || (x >= Box.xMin && x < Box.xMax && z >= Box.yMin && z < Box.yMax);
            public bool Contains(Vector2 p) => Contains(p.x, p.y);

            /// <summary>Does the plan rectangle (world x, z) touch the box?</summary>
            public bool Overlaps(Vector2 min, Vector2 max) =>
                Full || (max.x >= Box.xMin && min.x < Box.xMax && max.y >= Box.yMin && min.y < Box.yMax);

            /// <summary>Does tile (tx, tz) touch the box?</summary>
            public bool TileIn(int tx, int tz)
            {
                var mn = new Vector2(tx * CityMeshes.TileSize, tz * CityMeshes.TileSize);
                return Overlaps(mn, mn + Vector2.one * CityMeshes.TileSize);
            }

            /// <summary>Is the edge in this report: its tier counted, and
            /// some segment of it touching the box?</summary>
            public bool Takes(CityMap.Edge e)
            {
                if (!HasTier(CityTier.Of(e))) return false;
                if (Full) return true;
                var p = e.pts;
                for (int i = 0; i + 1 < p.Length; i++)
                    if (Overlaps(Vector2.Min(p[i], p[i + 1]), Vector2.Max(p[i], p[i + 1]))) return true;
                return p.Length == 1 && Contains(p[0]);
            }

            /// <summary>The tiles touching the box, for a report that builds
            /// them (city-wide: every tile under the map's edges).</summary>
            public void Tiles(CityMap map, List<Vector2Int> into)
            {
                into.Clear();
                float xMin, zMin, xMax, zMax;
                if (Full)
                {
                    xMin = zMin = float.MaxValue; xMax = zMax = float.MinValue;
                    foreach (var e in map.edges)
                        foreach (var q in e.pts)
                        {
                            xMin = Mathf.Min(xMin, q.x); xMax = Mathf.Max(xMax, q.x);
                            zMin = Mathf.Min(zMin, q.y); zMax = Mathf.Max(zMax, q.y);
                        }
                    if (xMin > xMax) return;
                }
                else { xMin = Box.xMin; zMin = Box.yMin; xMax = Box.xMax; zMax = Box.yMax; }
                int tx0 = Mathf.FloorToInt(xMin / CityMeshes.TileSize), tx1 = Mathf.FloorToInt(xMax / CityMeshes.TileSize);
                int tz0 = Mathf.FloorToInt(zMin / CityMeshes.TileSize), tz1 = Mathf.FloorToInt(zMax / CityMeshes.TileSize);
                for (int tz = tz0; tz <= tz1; tz++)
                    for (int tx = tx0; tx <= tx1; tx++)
                        into.Add(new Vector2Int(tx, tz));
            }

            /// <summary>One line for the top of the report.</summary>
            public string Describe()
            {
                var tl = new List<string>();
                for (int t = 1; t <= 3; t++) if (tiers[t]) tl.Add(CityTier.Short(t));
                string where = Full ? "CITY-WIDE (PSX_AUDIT_FULL=1)"
                    : string.Format(CultureInfo.InvariantCulture, "BOXED x {0:0}..{1:0}, z {2:0}..{3:0} ({4}; PSX_AUDIT_FULL=1 for city-wide)",
                                    Box.xMin, Box.xMax, Box.yMin, Box.yMax, BoxFrom);
                return $"{Name} scope: {where}, tiers {string.Join("+", tl)}" + (Problem != null ? $" - {Problem}" : "");
            }
        }

        /// <summary>The scope report <paramref name="name"/> (COVER, TWIN,
        /// PROFILE, PAINT, MERGE, LOT, BULB) runs in, from the environment
        /// (see <see cref="AuditScope"/>).</summary>
        public static AuditScope ScopeFor(string name)
        {
            string key = (name ?? "").Trim().ToUpperInvariant();
            string problem = null;
            bool full = System.Environment.GetEnvironmentVariable("PSX_AUDIT_FULL") == "1";
            Rect box = OwnerBox; string from = "default: uptown + W 5th/I-77";
            foreach (var v in new[] { "PSX_" + key + "_BOX", "PSX_AUDIT_BOX" })
            {
                string s = System.Environment.GetEnvironmentVariable(v);
                if (string.IsNullOrWhiteSpace(s)) continue;
                if (TryParseBox(s, out var b)) { box = b; from = v; }
                else problem = $"BAD {v}='{s}' (want x0,z0,x1,z1): {from} used";
                break;
            }
            var mask = new[] { false, true, true, true };
            foreach (var v in new[] { "PSX_" + key + "_TIER", "PSX_AUDIT_TIER" })
            {
                string s = System.Environment.GetEnvironmentVariable(v);
                if (string.IsNullOrWhiteSpace(s)) continue;
                if (!TryParseTiers(s, mask))
                {
                    mask = new[] { false, true, true, true };
                    problem = (problem != null ? problem + "; " : "") + $"BAD {v}='{s}' (want 1, 1,2 or all): all tiers used";
                }
                break;
            }
            return new AuditScope(key, full, box, full ? "" : from, mask, problem);
        }

        static bool TryParseBox(string s, out Rect box)
        {
            box = default;
            var p = s.Split(',');
            if (p.Length != 4) return false;
            var v = new float[4];
            for (int i = 0; i < 4; i++)
                if (!float.TryParse(p[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return false;
            box = Rect.MinMaxRect(Mathf.Min(v[0], v[2]), Mathf.Min(v[1], v[3]), Mathf.Max(v[0], v[2]), Mathf.Max(v[1], v[3]));
            return box.width > 0f && box.height > 0f;
        }

        static bool TryParseTiers(string s, bool[] mask)
        {
            s = s.Trim().ToLowerInvariant();
            if (s == "all") { mask[1] = mask[2] = mask[3] = true; return true; }
            mask[1] = mask[2] = mask[3] = false;
            bool any = false;
            foreach (var part in s.Split(',', '+', ' '))
            {
                var t = part.Trim().TrimStart('t');
                if (t.Length == 0) continue;
                if (!int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 1 || n > 3) return false;
                mask[n] = true; any = true;
            }
            return any;
        }
    }
}
