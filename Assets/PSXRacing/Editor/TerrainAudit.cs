using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Measures the built scene where the ground meets everything standing on
    /// it, by dropping rays onto it rather than by re-running the maths that
    /// put it there.
    ///
    /// Every fault this looks for is silent, and every one of them has shipped:
    ///
    ///   * ground through the road — the landscape grid is coarse and the
    ///     tarmac rides 12 cm above it, so a crest too tight for the grid
    ///     resolution carpets the racing line in triangles of hillside;
    ///   * a gorge that was never dug — a bridge whose terrain carve did not
    ///     happen still looks completely normal from the driving line, because
    ///     from up there a bridge is only road;
    ///   * daylight under a building — the reason this file exists. These
    ///     meshes are hollow shells with no floor, so a base level with the
    ///     ground is a base you can see straight in under, and on sloping
    ///     ground the downhill corner hangs in the air;
    ///   * grass through the shoulder — the lattice sunk under the exact
    ///     shoulder surface beside the road coming up to meet it, or through.
    ///
    /// Whether a car can get back on, fall off, or is walled in without
    /// cause is TrackObstacleAudit's edge pass; bridge rails included.
    ///
    /// Rays, not formulas: the builder already believes its own arithmetic, and
    /// a second copy of it would agree with the first while both were wrong.
    /// What this asks is what a wheel would find.
    ///
    /// Menu: PSX Racing/Audit Terrain.
    /// </summary>
    public static class TerrainAudit
    {
        /// <summary>Metres of ground the road has to stand clear of before it
        /// counts as buried. The ribbon sits 12 cm up, so anything that leaves
        /// under 2 cm is about to poke through.</summary>
        const float RoadClearMin = 0.02f;
        /// <summary>How far under a bridge deck the ground has to fall away
        /// before the span reads as a span rather than as a hump.</summary>
        const float BridgeDropMin = 3f;

        /// <summary>How far the land has to leave the road's level before the
        /// shoulder stops being somewhere you can drive. A metre is about the
        /// step a car takes without either stopping or leaving the ground —
        /// past that you are off the road rather than beside it.</summary>
        const float AprunEdgeM = 1.0f;
        /// <summary>How far out to bother looking. Past twenty metres it is
        /// not a shoulder by any reading, and the answer is already bad.
        /// </summary>
        const float AprunReachM = 20f;
        /// <summary>The average drivable shoulder past which the width is worth
        /// a remark. REPORTED, NEVER FAILED: this used to FAIL a stage whose
        /// shoulder averaged more than 2.5 m, verify counted that FAIL, and the
        /// cheapest way to pass it was a wall on level ground — which is how
        /// the parkway came to have 1.2 km of "open verge" walls with drivable
        /// land behind them. The owner, 2026-09-13: "Roads sitting cm above the
        /// ground do not need rails/walls, they should meet the ground properly
        /// by DOT standards." A wide recoverable shoulder is the standard, not
        /// the fault; whether a barrier is warranted is RoadsideRules' question
        /// and TrackObstacleAudit's edge pass asks it.</summary>
        const float AprunOkM = 2.5f;

        /// <summary>The ground lattice has to sit at least this far under the
        /// shoulder surface wherever both are present, or the coarse grid is
        /// one crest away from showing through it (RoadsideRules.HideMarginM
        /// is what the builder pins it to; this is the floor the audit holds,
        /// the same 3 cm margin as the 0.03 in synthesis 7.4).</summary>
        const float LatticeUnderMinM = 0.03f;
        /// <summary>Ground above the shoulder surface by more than this is
        /// grass through the shoulder: the first thing a ray from above meets.
        /// </summary>
        const float GrassThroughM = 0.01f;
        /// <summary>Lateral pitch of the shoulder probes.</summary>
        const float ShoulderPitchM = 0.25f;
        /// <summary>
        /// Longest run at the shoulder's OUTER end, inward of the toe tuck,
        /// over which the lattice may come within <see cref="LatticeUnderMinM"/>
        /// of the shoulder (or cross it) without being counted — the designed
        /// crossing, not a fault.
        ///
        /// The builders do not hold the full hide margin to the toe and then
        /// dive: the stage section fades its margin to nothing over the last
        /// 1.5 m before the catch (StageHideFadeM) so the lattice meets the
        /// toe at its own height, and on a catch that grazes falling land the
        /// two surfaces are within 3 cm for longer still. A python replica of
        /// that design (catches 2-9 m out, lattice-to-section relative slopes
        /// of 0.02-0.2) failed 1446 of 2400 half-sections when only the tuck's
        /// own probes were excused. With a trailing crossing of up to this it
        /// failed 33, every one a catch within 2.5 m of the tarmac on land
        /// grazing the section at 2-3%, where no probe on the whole ribbon has
        /// the margin — which is the case this check exists to count — and it
        /// still failed a crest through the middle of the shoulder, a ribbon
        /// grassed end to end, and a lattice 2 cm under the whole shoulder.
        /// </summary>
        const float CrossingMaxM = 2.0f;
        /// <summary>Furthest a shoulder ribbon is followed out. A fill's
        /// foreslope can run a long way to its catch.</summary>
        const float ShoulderReachM = 30f;
        /// <summary>Where "is this road benched into a slope" is asked, and how
        /// far apart its two sides have to be for the answer to be yes. Past
        /// the corridor shelf and well into the blend, so what is being
        /// compared is the real land on each side.</summary>
        const float AprunReliefM = 38f, AprunBenchM = 8f;

        [MenuItem("PSX Racing/Audit Terrain")]
        public static void Run()
        {
            var log = new StringBuilder();
            int failures = 0;
            foreach (var def in TrackCatalog.Scened) failures += AuditOne(def, log);
            log.AppendLine(failures == 0 ? "TERRAIN AUDIT OK" : "TERRAIN AUDIT: " + failures + " PROBLEM(S)");
            Debug.Log(log.ToString());
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "../PSXRacing_terrain_audit.txt"),
                log.ToString());
        }

        static int AuditOne(TrackCatalog.TrackDef def, StringBuilder log)
        {
            // The city has no baked ground to ray — its terrain is generated
            // per tile at runtime and audited by CityAudit. Counting its
            // by-design empty scene as a failure buried every real result
            // under a permanent "1 PROBLEM(S)".
            if (def.city) return 0;

            string scenePath = "Assets/PSXRacing/Scenes/" + def.id + ".unity";
            if (!System.IO.File.Exists(scenePath))
            {
                log.AppendLine("MISSING SCENE " + scenePath);
                return 1;
            }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var path = Object.FindFirstObjectByType<TrackPath>();
            var ground = GameObject.Find("Track/Ground");
            var road = GameObject.Find("Track/Road");
            if (path == null || path.Count == 0 || ground == null || road == null)
            {
                log.AppendLine(def.id + ": no track path, no ground or no road");
                return 1;
            }
            // Every ground MESH, not every ground COLLIDER.
            //
            // This gathered colliders for its whole life, and that is exactly
            // how the stage's far mountain chunks — 60 m cells, renderer-only,
            // no collision because nothing drivable ever reaches them — sat in
            // the middle of the parkway for a week with the audit reporting a
            // clean pass every run. A hillside you can drive through still
            // hides the corner. Temporary colliders go on for the audit and
            // come off below; the scene is never saved.
            var groundCols = new List<Collider>();
            var temporary = new List<MeshCollider>();
            foreach (var mf in ground.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var col = mf.GetComponent<Collider>();
                if (col == null)
                {
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    temporary.Add(mc);
                    col = mc;
                }
                groundCols.Add(col);
            }
            var roadCol = road.GetComponent<Collider>();
            if (groundCols.Count == 0 || roadCol == null)
            {
                log.AppendLine(def.id + ": ground has no mesh, or road has no collider");
                return 1;
            }

            log.AppendLine("=== " + def.id + " ===");
            int problems = 0;

            // ---- 1. the ground never comes up through the tarmac ----
            // Sampled across the WIDTH, not only down the centreline. A crest
            // that carpets the outside of a corner leaves the middle of the
            // lane perfectly clean, so the centreline-only version of this
            // passed every single run while the corner was unreadable from the
            // driving seat.
            float half = Mathf.Max(1f, path.roadWidth * 0.5f - 0.6f);
            float worstClear = float.MaxValue;
            int worstIdx = -1;
            string worstWhat = null;
            int buried = 0, probes = 0;
            float lowY = float.MaxValue, highY = float.MinValue;
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 wp = path.GetPoint(i);
                lowY = Mathf.Min(lowY, wp.y); highY = Mathf.Max(highY, wp.y);
                // Skip the spans that are SUPPOSED to have nothing under them —
                // but ONLY the full-depth ones.
                //
                // This used to exempt every station with any bridge blend at
                // all, which is the whole approach ramp at both ends of every
                // span, and that is exactly where the ground came through: the
                // release toward the real slope was free to raise the hillside
                // over the road as well as drop it away, and it did so at all
                // eight parkway abutments while this audit reported 4482 clean
                // probes. The exemption was never needed to pass, either — a
                // station under an open span finds its ground fifteen metres
                // down and clears by fifteen metres. What it bought was
                // silence.
                if (BlendAt(def, i, path.spacing) > 0.98f) continue;
                Vector3 right = RightAt(path, i);
                foreach (float off in new[] { -half, 0f, half })
                {
                    Vector3 at = wp + right * off;
                    // Both surfaces measured, so the answer needs no copy of
                    // the builder constant that lifts the ribbon off the
                    // ground.
                    if (!DropAny(groundCols, at, out float gy, out string what)) continue;
                    if (!Drop(roadCol, at, out float ry)) continue;
                    probes++;
                    float clear = ry - gy;
                    if (clear < RoadClearMin) buried++;
                    if (clear < worstClear) { worstClear = clear; worstIdx = i; worstWhat = what; }
                }
            }
            if (worstIdx >= 0)
            {
                bool ok = worstClear >= RoadClearMin;
                if (!ok) problems++;
                log.AppendLine(string.Format(
                    "  {0} road stands {1:0.000} m clear of the ground at its tightest " +
                    "(waypoint {2}, {3}); {4} of {5} probes buried",
                    ok ? "ok  " : "FAIL", worstClear, worstIdx, worstWhat ?? "?", buried, probes));
            }
            log.AppendLine(string.Format("  ..   climbs {0:0.0} m ({1:0.0} to {2:0.0})",
                highY - lowY, lowY, highY));

            // ---- 2. every bridge has a hole under it ----
            if (def.bridges != null && def.bridges.Length > 0)
            {
                float deepest = 0f;
                int spanWaypoints = 0;
                for (int i = 0; i < path.Count; i++)
                {
                    if (BlendAt(def, i, path.spacing) < 0.98f) continue;
                    spanWaypoints++;
                    Vector3 wp = path.GetPoint(i);
                    // From UNDER the deck, so the deck itself is not what the
                    // ray finds first.
                    if (!DropAny(groundCols, wp - Vector3.up * 2.5f, out float gy)) continue;
                    deepest = Mathf.Max(deepest, wp.y - gy);
                }
                bool ok = deepest >= BridgeDropMin && spanWaypoints > 0;
                if (!ok) problems++;
                log.AppendLine(string.Format(
                    "  {0} gorge is {1:0.0} m deep under {2} waypoints of full-depth span",
                    ok ? "ok  " : "FAIL", deepest, spanWaypoints));

                var decks = new List<GameObject>();
                foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                    if (mf.gameObject.name.StartsWith("BridgeDeck")) decks.Add(mf.gameObject);
                bool deckOk = decks.Count == def.bridges.Length;
                if (!deckOk) problems++;
                log.AppendLine(string.Format("  {0} {1} deck(s) built for {2} span(s)",
                    deckOk ? "ok  " : "FAIL", decks.Count, def.bridges.Length));
            }

            // ---- 3. nothing you can see under ----
            float worstGap = float.MinValue;
            string worstName = null;
            int floating = 0, sampled = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var go = r.gameObject;
                if (go.name != "Building" && !go.name.StartsWith("Parked_")
                    && go.name != "Gas_station" && go.name != "Tree") continue;
                // Walk up to the thing the builder actually POSITIONED, which
                // is always a direct child of Scenery. Stopping one level short
                // measures a sub-object against the ground instead of against
                // what it is standing on: the forecourt props sit on the
                // station slab a metre up, and this reported them as floating.
                Transform root = go.transform;
                while (root.parent != null && root.parent.name != "Scenery") root = root.parent;

                var b = WorldBounds(root.gameObject);
                if (b.size.sqrMagnitude < 0.01f) continue;
                sampled++;
                // The corners of the footprint. A block set into a hill is fine
                // at its middle and hanging by 2 m at one corner, which is
                // exactly the shape of the fault.
                float gap = float.MinValue;
                for (int c = 0; c < 4; c++)
                {
                    float x = (c & 1) == 0 ? b.min.x : b.max.x;
                    float z = (c & 2) == 0 ? b.min.z : b.max.z;
                    // Pull in slightly so a corner exactly on a facet edge does
                    // not miss the mesh entirely.
                    x = Mathf.Lerp(x, b.center.x, 0.06f);
                    z = Mathf.Lerp(z, b.center.z, 0.06f);
                    if (!DropAny(groundCols, new Vector3(x, b.max.y + 5f, z), out float gy)) continue;
                    gap = Mathf.Max(gap, gy - b.min.y);   // >0 means buried
                }
                if (gap == float.MinValue) continue;
                float showing = -gap;                     // >0 means daylight under it
                if (showing > worstGap) { worstGap = showing; worstName = root.name; }
                if (showing > 0.05f) floating++;
            }

            // ---- 4. how wide the shoulder is (reported, not failed) ----
            //
            // Written as a FAIL for "most of the blue ridge does not have big
            // runoff areas to drive onto", and demoted to a report by the
            // owner's later rule that a road should meet the ground by DOT
            // standards rather than be walled in (see AprunOkM). What follows is
            // the history of the measurement, which is still worth having.
            //
            // The parkway had wide run-off everywhere: the terrain corridor
            // holds a flat shelf sixteen metres either side of the centreline,
            // and on a road nine and a half metres wide that is eleven metres
            // of level gravel you can leave the road onto, sit on, and drive
            // back off.
            //
            // Nothing already here could see it. TerrainAudit asked whether the
            // ground came UP through the tarmac; the obstacle audit asked what
            // stood inside the barrier line; neither asks the question a driver
            // asks, which is HOW FAR OUT can I go before the world stops me.
            // So this walks outward from the tarmac edge until either something
            // solid is in the way or the land has left the road's level by
            // AprunEdgeM, and reports the width it found.
            //
            // Stages only. A closed circuit is SUPPOSED to have run-off — that
            // is what a run-off area IS — and measuring one against a mountain
            // road's shoulder would be a permanent failure with nothing behind
            // it.
            //
            // And only where the road is BENCHED INTO A SLOPE, which is the
            // thing being complained about: hillside one side, valley the
            // other, and a strip of level ground either side of the tarmac that
            // the corridor put there. Asked as "are the two sides of this road
            // at different heights", because that is what a bench IS and
            // nothing else answers it honestly.
            //
            // Two weaker rules were tried first and both mislabelled a beach.
            // Flat-vs-not fails Emerald Isle (a drag strip across a sandbar)
            // and both causeways (salt flat) for being flat — thirteen metres
            // of drivable shoulder there is not a bench, it is the island. And
            // "is there relief somewhere off to the side" fails a causeway
            // approach for having water 4 m down and 38 m out, which is also
            // just the island. On an embankment both sides are equally low, so
            // the difference between them is what tells the two apart.
            if (def.stage && def.stageInCity)
            {
                // A street venue's verge is the city's: three lanes and a
                // paved shoulder on the 277, and the CLT theme builds no wall
                // on flat ground BY DESIGN (a wall chord cuts across a 22 m
                // ramp). Measured against a mountain road's shoulder it read
                // 6.4 m on the loop and 6.2 m on Independence, which is what
                // a freeway verge is, not a builder fault.
                log.AppendLine("  ..   a city street keeps its verge — shoulder not measured");
            }
            else if (def.stage)
            {
                float roadHalf = path.roadWidth * 0.5f;
                double total = 0.0;
                float widest = 0f;
                int widestIdx = -1, counted = 0, wide = 0, flat = 0;
                for (int i = 0; i < path.Count; i++)
                {
                    Vector3 wp = path.GetPoint(i);
                    Vector3 right = RightAt(path, i);
                    if (!DropAny(groundCols, wp - right * AprunReliefM, out float lo) ||
                        !DropAny(groundCols, wp + right * AprunReliefM, out float hi) ||
                        Mathf.Abs(hi - lo) < AprunBenchM)
                    { flat += 2; continue; }

                    foreach (float side in new[] { -1f, 1f })
                    {
                        float apron = 0f;
                        for (float d = roadHalf + 0.4f; d <= AprunReachM; d += 0.35f)
                        {
                            Vector3 at = wp + right * side * d;
                            if (Stopped(at, wp.y, groundCols, roadCol)) break;
                            if (!DropAny(groundCols, at, out float gy)) break;
                            if (Mathf.Abs(gy - wp.y) > AprunEdgeM) break;
                            apron = d - roadHalf;
                        }
                        total += apron;
                        counted++;
                        if (apron > AprunOkM) wide++;
                        if (apron > widest) { widest = apron; widestIdx = i; }
                    }
                }
                if (counted > 0)
                {
                    // INFO, never FAIL: see AprunOkM for why a wide shoulder
                    // stopped being a fault.
                    float mean = (float)(total / counted);
                    log.AppendLine(string.Format(
                        "  info shoulder is drivable for {0:0.0} m past the tarmac on average " +
                        "(widest {1:0.0} m at waypoint {2}); {3} of {4} shoulders on a benched road " +
                        "over {5:0.0} m, {6} not benched and not counted",
                        mean, widest, widestIdx, wide, counted, AprunOkM, flat));
                }
                else
                {
                    log.AppendLine("  ..   nothing benched into a slope — no shoulder to measure");
                }
            }

            problems += AuditShoulderLattice(path, groundCols, log);

            foreach (var mc in temporary) Object.DestroyImmediate(mc);
            if (sampled > 0)
            {
                bool ok = floating == 0;
                if (!ok) problems++;
                log.AppendLine(string.Format(
                    "  {0} {1} of {2} props show daylight underneath (worst {3:0.00} m, {4})",
                    ok ? "ok  " : "FAIL", floating, sampled, Mathf.Max(0f, worstGap), worstName));
            }

            return problems;
        }

        /// <summary>
        /// THE LATTICE STAYS UNDER THE SHOULDER, AND NO GRASS COMES THROUGH IT.
        ///
        /// The owner's rule (RoadsideRules) puts an exact shoulder surface
        /// beside the tarmac — "RoadEdge*", its own MeshCollider — and sinks the
        /// coarse ground lattice under it. That is what lets a road sit an inch
        /// above its shoulder instead of a slab above a trench without the grid
        /// clipping through. Two things go wrong with it and neither shows from
        /// the builder's side: the lattice comes up to within a z-fight of the
        /// shoulder (one sag of the 12 m grid from showing), or it comes
        /// THROUGH, and the first thing an eye or a wheel meets across the
        /// shoulder is grass.
        ///
        /// Rays, like the rest of this file: onto the shoulder colliders and
        /// onto the ground meshes, per station, both sides, every quarter metre
        /// from the kerb strip's outer edge out along the ribbon while there is
        /// one. Both tests stop short of the ribbon's outer toe by
        /// RoadsideRules.ToeTuckRunM (and one probe pitch, for where between two
        /// probes it really ended), and short of the crossing that leads into
        /// it (<see cref="CrossingMaxM"/>), because there the shoulder is
        /// SUPPOSED to meet and then cross under the lattice: two continuous
        /// surfaces that cross leave a car nothing to catch on, which is the
        /// point of tucking it. The crossing is excused only where it is one —
        /// trailing into the toe with a healthy margin somewhere inward of it;
        /// a lattice that is close or through along the whole ribbon is counted
        /// in full.
        ///
        /// THE TOE IS THIS STATION'S OWN. Where "the ribbon's outer toe" is was
        /// read as where the rays stop finding RoadEdge, and two places carry a
        /// ray on past it onto somebody else's ribbon: a deck station, whose
        /// section is empty, where a ray from over the deck falls through to
        /// the road passing UNDER the bridge (Blowing Rock's self-crossing,
        /// wp 2432-2437: the lower road's shoulder crossed at a slant, its
        /// tucks and skirts included), and the inside of a bend, where the
        /// zipper fans of the neighbouring stations cross this line past its
        /// skirt (Little Switzerland wp 716 L: its own skirt at 7.62 m, then
        /// fans and other stations' sections out to 12.15 m, so the toe excused
        /// was 12 m out and its own tuck counted as grass). A python replica of
        /// this pass on the round-one meshes reproduced its counts exactly, and
        /// those were 60 of Blowing Rock's 106 close-or-through probes (36 of
        /// its 60 grass) and 24 of Little Switzerland's 103. A tuck is BUILT to
        /// go under the lattice (RoadsideRules.ToeTuckM), so counting one is
        /// counting the design.
        ///
        /// So a probe is this station's shoulder only while the RoadEdge
        /// triangle it lands on has a corner on this station's own cross-section
        /// line (<see cref="OwnSection"/>): BuildShoulders zips each quad from
        /// the points of its two stations, so every triangle of the ribbon
        /// either side of a station has one there, and nothing another station
        /// or another road built does. The first probe that is not ends the
        /// section, and the toe is excused from there. Every triangle of every
        /// ribbon is still probed along its own stations' lines, which is all
        /// this pass ever probed on purpose.
        /// </summary>
        static int AuditShoulderLattice(TrackPath path, List<Collider> groundCols, StringBuilder log)
        {
            var shoulderCols = new List<Collider>();
            foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
                if (col != null && !col.isTrigger && col.gameObject.name.StartsWith("RoadEdge"))
                    shoulderCols.Add(col);
            if (shoulderCols.Count == 0)
            {
                log.AppendLine("  ..   no RoadEdge shoulder surface in this scene - lattice under the shoulder not measured");
                return 0;
            }
            var shoulders = new ColliderSet(shoulderCols);
            var ground = new ColliderSet(groundCols);

            float roadHalf = path.roadWidth * 0.5f;
            float kerb = PSXRacingBuilder.KerbWidth;
            int toeProbes = Mathf.CeilToInt((RoadsideRules.ToeTuckRunM + ShoulderPitchM) / ShoulderPitchM);
            int crossProbes = Mathf.CeilToInt(CrossingMaxM / ShoulderPitchM);
            int probes = 0, close = 0, grass = 0, crossing = 0, notOwn = 0;
            float tightest = float.MaxValue, worstGrass = 0f;
            string tightAt = null, grassAt = null;
            var sy = new List<float>();
            var gy = new List<float>();
            var gOn = new List<string>();
            var meshData = new Dictionary<Mesh, (Vector3[] verts, int[] tris)>();
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 wp = path.GetPoint(i);
                Vector3 right = SectionRightAt(path, i);
                // From a few metres over the road, not from 200 m: on a
                // switchback the ribbon of the road above is a different
                // shoulder, and the question is about this one.
                float fromY = wp.y + 6f;
                foreach (float side in new[] { -1f, 1f })
                {
                    sy.Clear(); gy.Clear(); gOn.Clear();
                    for (float e = kerb + ShoulderPitchM * 0.5f; e <= ShoulderReachM; e += ShoulderPitchM)
                    {
                        Vector3 at = wp + right * side * (roadHalf + e);
                        if (!shoulders.Drop(at, fromY, 60f, out float s, out _, out RaycastHit top)) break;
                        // Past this station's own section: another station's
                        // fan, or another road's shoulder under a deck.
                        if (!OwnSection(top, wp, right, side, meshData)) { notOwn++; break; }
                        sy.Add(s);
                        bool hasGround = ground.Drop(at, fromY, 60f, out float g, out string what);
                        gy.Add(hasGround ? g : float.NaN);
                        gOn.Add(what);
                    }
                    // The designed crossing: inward from the toe while the
                    // lattice is within the margin, as far as CrossingMaxM —
                    // and only if a probe further in has the full margin, so a
                    // ribbon the lattice is through from end to end still counts.
                    int toe = Mathf.Max(0, sy.Count - toeProbes), inner = toe;
                    while (inner > 0 && toe - inner < crossProbes && !float.IsNaN(gy[inner - 1]) &&
                           sy[inner - 1] - gy[inner - 1] < LatticeUnderMinM)
                        inner--;
                    bool healthy = false;
                    for (int k = 0; k < inner && !healthy; k++)
                        healthy = !float.IsNaN(gy[k]) && sy[k] - gy[k] >= LatticeUnderMinM;
                    if (!healthy) inner = toe;
                    crossing += toe - inner;
                    for (int k = 0; k < inner; k++)
                    {
                        if (float.IsNaN(gy[k])) continue;      // a hole in the lattice is not the lattice showing
                        probes++;
                        float under = sy[k] - gy[k];
                        bool isClose = under < LatticeUnderMinM, isGrass = -under > GrassThroughM;
                        if (isClose) close++;
                        if (isGrass) grass++;
                        if (under >= tightest && !(isGrass && -under > worstGrass)) continue;
                        string where = string.Format("waypoint {0} {1}, {2:0.00} m past the tarmac edge, under {3}",
                                                     i, side < 0f ? "L" : "R", kerb + ShoulderPitchM * (k + 0.5f), gOn[k]);
                        if (under < tightest) { tightest = under; tightAt = where; }
                        if (isGrass && -under > worstGrass) { worstGrass = -under; grassAt = where; }
                    }
                }
            }
            if (probes == 0)
            {
                log.AppendLine("  ..   a RoadEdge shoulder, but no ground lattice under it to compare");
                return 0;
            }
            bool closeOk = close == 0, grassOk = grass == 0;
            log.AppendLine(string.Format(
                "  {0} ground lattice stays at least {1:0.00} m under the shoulder surface on {2} of {3} probes " +
                "(tightest {4:+0.000;-0.000} m, {5}); {6} probes in the designed crossing into the toe not counted; " +
                "{7} half-sections end where the ribbon under the probe becomes another station's or another road's",
                closeOk ? "ok  " : "FAIL", LatticeUnderMinM, probes - close, probes, tightest, tightAt, crossing, notOwn));
            log.AppendLine(string.Format(
                "  {0} grass through the shoulder: {1} of {2} probes meet Ground more than {3:0.00} m over the RoadEdge surface{4}",
                grassOk ? "ok  " : "FAIL", grass, probes, GrassThroughM,
                grassOk ? "" : string.Format(" (worst {0:0.000} m, {1})", worstGrass, grassAt)));
            return (closeOk ? 0 : 1) + (grassOk ? 0 : 1);
        }

        /// <summary>A set of colliders with their bounds read once: a stage has
        /// hundreds of ground chunks, and asking each for its bounds on every
        /// probe of a 20 km roadside is most of the cost of this pass.</summary>
        sealed class ColliderSet
        {
            readonly Collider[] cols;
            readonly Bounds[] bounds;

            public ColliderSet(List<Collider> from)
            {
                cols = from.ToArray();
                bounds = new Bounds[cols.Length];
                for (int k = 0; k < cols.Length; k++) bounds[k] = cols[k].bounds;
            }

            /// <summary>Highest hit straight down from <paramref name="fromY"/>
            /// over (at.x, at.z), and the name of what answered.</summary>
            public bool Drop(Vector3 at, float fromY, float reach, out float y, out string what) =>
                Drop(at, fromY, reach, out y, out what, out _);

            /// <summary>The same, with the winning hit itself (its collider and
            /// triangle).</summary>
            public bool Drop(Vector3 at, float fromY, float reach, out float y, out string what, out RaycastHit best)
            {
                y = float.MinValue; what = null; best = default;
                var ray = new Ray(new Vector3(at.x, fromY, at.z), Vector3.down);
                bool any = false;
                for (int k = 0; k < cols.Length; k++)
                {
                    var b = bounds[k];
                    if (at.x < b.min.x || at.x > b.max.x || at.z < b.min.z || at.z > b.max.z) continue;
                    if (b.max.y < fromY - reach || b.min.y > fromY) continue;
                    if (!cols[k].Raycast(ray, out RaycastHit hit, reach) || hit.point.y <= y) continue;
                    y = hit.point.y;
                    what = cols[k].gameObject.name;
                    best = hit;
                    any = true;
                }
                return any;
            }
        }

        /// <summary>How far in plan a RoadEdge vertex may lie from a station's
        /// cross-section line and still be one of that station's points. The
        /// builder lays them ON the line (the waypoint plus RightAt times e),
        /// so this is float noise with room to spare; a neighbouring station's
        /// points are a station spacing away, and still most of a metre away
        /// where the inside of a bend clips a section to keep the lines apart
        /// (ShoulderInsideBendReach).</summary>
        const float OwnSectionTolM = 0.05f;

        /// <summary>
        /// Is the RoadEdge triangle under this probe part of the ribbon either
        /// side of THIS station — does it have a corner on the station's own
        /// cross-section line, on this side of the road? BuildShoulders zips
        /// every quad from the points of its two stations, so each of its
        /// triangles has a corner on both lines. True when it cannot tell (not
        /// a mesh collider, an unreadable mesh, no triangle index), which is
        /// this pass's behaviour before the test existed.
        /// </summary>
        static bool OwnSection(RaycastHit hit, Vector3 wp, Vector3 right, float side,
                               Dictionary<Mesh, (Vector3[] verts, int[] tris)> cache)
        {
            var mc = hit.collider as MeshCollider;
            if (mc == null || mc.sharedMesh == null || hit.triangleIndex < 0) return true;
            var mesh = mc.sharedMesh;
            if (!cache.TryGetValue(mesh, out var data))
            {
                data = mesh.isReadable ? (mesh.vertices, mesh.triangles) : (null, null);
                cache[mesh] = data;
            }
            if (data.verts == null || 3 * hit.triangleIndex + 2 >= data.tris.Length) return true;
            var t = mc.transform;
            for (int k = 0; k < 3; k++)
            {
                Vector3 v = t.TransformPoint(data.verts[data.tris[3 * hit.triangleIndex + k]]) - wp;
                float off = v.x * right.z - v.z * right.x;      // off the line, in plan
                if (Mathf.Abs(off) <= OwnSectionTolM && (v.x * right.x + v.z * right.z) * side > 0f) return true;
            }
            return false;
        }

        /// <summary>The direction a station's cross-section is laid along, the
        /// way the builder's RightAt lays it: the chord from the station before
        /// to the station after, wrapping on a circuit and clamped at a route's
        /// ends (TrackPath.GetPoint does both). <see cref="RightAt"/> clamps on
        /// a loop too, which at the start line turns the probe line off the
        /// section by a degree or two — nothing to the other passes, and enough
        /// to lose a section's own points 20 m out.</summary>
        static Vector3 SectionRightAt(TrackPath path, int i)
        {
            Vector3 t = path.GetPoint(i + 1) - path.GetPoint(i - 1);
            t.y = 0f;
            if (t.sqrMagnitude < 1e-6f) return RightAt(path, i);
            return Vector3.Cross(Vector3.up, t.normalized).normalized;
        }

        /// <summary>Bridge blend at a waypoint index. A strip has none.</summary>
        static float BlendAt(TrackCatalog.TrackDef def, int i, float spacing) =>
            def.drag ? 0f : TrackCatalog.BridgeBlend(def, Mathf.Repeat(i * spacing, Mathf.Max(def.LengthM, 1f)));

        /// <summary>Straight down onto ONE collider, from well above. Raycasts
        /// the ground specifically rather than the scene, so a wall, a kerb or
        /// the deck itself cannot answer a question about the land.</summary>
        static bool Drop(Collider ground, Vector3 from, out float y)
        {
            y = 0f;
            var ray = new Ray(from + Vector3.up * 200f, Vector3.down);
            if (!ground.Raycast(ray, out RaycastHit hit, 600f)) return false;
            y = hit.point.y;
            return true;
        }

        /// <summary>The same drop against a SET of ground colliders (the
        /// stage's chunks), keeping the HIGHEST hit and naming what answered —
        /// in the near/far overlap ring both answer, and which of the two is
        /// the one standing in the road is the whole diagnosis.</summary>
        static bool DropAny(List<Collider> grounds, Vector3 from, out float y, out string what)
        {
            y = float.MinValue; what = null;
            bool any = false;
            foreach (var g in grounds)
            {
                var b = g.bounds;
                if (from.x < b.min.x - 1f || from.x > b.max.x + 1f ||
                    from.z < b.min.z - 1f || from.z > b.max.z + 1f) continue;
                if (Drop(g, from, out float gy) && gy > y) { y = gy; what = g.gameObject.name; any = true; }
            }
            return any;
        }

        static bool DropAny(List<Collider> grounds, Vector3 from, out float y) =>
            DropAny(grounds, from, out y, out _);

        static readonly Collider[] Overlap = new Collider[16];

        /// <summary>
        /// Is there something solid standing here that a car would hit?
        ///
        /// Deliberately does NOT look for walls by name. A guard wall, a cut
        /// bank, a bridge parapet and a tree with a trunk collider all stop a
        /// car identically, and a rule that named the two we happened to think
        /// of would keep passing as the third was added. So: anything that is
        /// not the ground and not the road, with something of it standing at
        /// least 30 cm above the road's level — which is what excludes the
        /// kerb, whose whole job is to be flush enough to drive over.
        /// </summary>
        static bool Stopped(Vector3 at, float roadY, List<Collider> grounds, Collider road)
        {
            int n = Physics.OverlapSphereNonAlloc(
                new Vector3(at.x, roadY + 0.75f, at.z), 0.4f, Overlap,
                ~0, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++)
            {
                var c = Overlap[k];
                if (c == null || c == road || grounds.Contains(c)) continue;
                if (c.bounds.max.y - roadY < 0.3f) continue;
                return true;
            }
            return false;
        }

        /// <summary>Road-right at a waypoint, from the neighbouring points.
        /// Clamped rather than wrapped: one sample either side of the seam on a
        /// loop is not worth a special case in an audit.</summary>
        static Vector3 RightAt(TrackPath path, int i)
        {
            int a = Mathf.Max(0, i - 1), b = Mathf.Min(path.Count - 1, i + 1);
            Vector3 t = path.GetPoint(b) - path.GetPoint(a);
            t.y = 0f;
            if (t.sqrMagnitude < 1e-6f) return Vector3.right;
            return Vector3.Cross(Vector3.up, t.normalized).normalized;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<MeshRenderer>();
            if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
