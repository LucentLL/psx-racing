using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// MEASURES THE ROAD'S EDGE AS A CAR MEETS IT, on every built venue.
    ///
    /// Written for "it is difficult to drive back onto tracks, especially the
    /// mountain tracks because of their walls; roads stick out of the ground;
    /// most roads aren't more than an inch above the shoulder dirt; all
    /// sections of bridges should have walls". Four questions per station and
    /// side, each answered with rays against the SAVED scene's colliders:
    ///
    ///   LIP      how far the first surface past the tarmac edge sits below
    ///            the tarmac (0.25 / 0.5 / 1 / 2 / 3 m out), and the steepest
    ///            step up a car driving back in has to climb. EVERY surface:
    ///            a hit steeper than normal.y 0.5 is a FACE, and it is the
    ///            lip, not something to look past (the first version skipped
    ///            them, read the dirt under the stage's 61-degree shoulder
    ///            batter, and reported a lip the car never met). Faces are
    ///            counted separately so the two can be told apart;
    ///   BARRIER  where the first wall-like face stands (a horizontal ray at
    ///            wheel height, any collider whose hit normal is not a floor);
    ///   POCKET   whether there is ground a car can stand on BEHIND that
    ///            barrier, within RoadsideRules.PocketBandM of road height —
    ///            the place a car gets to through a gap and cannot come back
    ///            from;
    ///   OPEN     an edge with no barrier within 3 m and nothing to land on
    ///            within RoadsideRules.OpenDropM below the tarmac: somewhere
    ///            to fall off.
    ///
    /// Every station (4 m). At every other one it could not see a stage deck
    /// end whose first 7.75 m had no parapet, which is the gap it was run to
    /// find. The thresholds are RoadsideRules', so this probe and the audits
    /// that fail a build agree about what they are counting.
    ///
    /// Writes PSXRacing_edge_probe.txt (a summary per venue) and
    /// edge_probe.csv (every sample). PSX_PROBE_ONLY=Id1,Id2 restricts the
    /// venues. Menu: PSX Racing/Probe Road Edges.
    /// </summary>
    public static class EdgeProbe
    {
        const int Every = 1;               // waypoints between sections (every 4 m station)
        const float OpenReach = 3.0f;      // a barrier further out than this is not guarding the edge
        const float OpenDrop = RoadsideRules.OpenDropM;       // a fall at least this deep beside an unguarded edge
        const float PocketBand = RoadsideRules.PocketBandM;   // ground behind a barrier within this of road height
        /// <summary>A hit whose normal.y is under this is a FACE, not a floor.
        /// Counted, never skipped.</summary>
        const float FaceNormalY = 0.5f;
        /// <summary>A horizontal ray's hit at or under this |normal.y| is a
        /// barrier: CollisionResponder.LandingNormalDot (0.7, private there),
        /// the line TrackObstacleAudit's edge pass draws too, so this probe's
        /// barriers and pockets are the audit's. It was 0.6 here, which let a
        /// 46-53 degree face be a wall to the car and a floor to the probe.
        /// </summary>
        const float WallNormalY = 0.7f;

        static readonly float[] LipAt = { 0.25f, 0.5f, 1f, 2f, 3f };

        [MenuItem("PSX Racing/Probe Road Edges")]
        public static void Run()
        {
            var only = System.Environment.GetEnvironmentVariable("PSX_PROBE_ONLY");
            var want = string.IsNullOrEmpty(only) ? null : new HashSet<string>(only.Split(','));
            var log = new StringBuilder();
            var csv = new StringBuilder("venue,wp,side,s,bridge,tarmacY,lip025,lip05,lip1,lip2,lip3,face1,faceHits,maxStepIn,barrierD,barrierName,pocket,open\n");
            foreach (var def in TrackCatalog.Scened)
            {
                if (want != null && !want.Contains(def.id)) continue;
                try { ProbeOne(def, log, csv); }
                catch (System.Exception ex) { log.AppendLine(def.id + ": THREW " + ex); }
            }
            string root = System.IO.Path.GetDirectoryName(Application.dataPath);
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "PSXRacing_edge_probe.txt"), log.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "edge_probe.csv"), csv.ToString());
            Debug.Log(log.ToString());
        }

        static void ProbeOne(TrackCatalog.TrackDef def, StringBuilder log, StringBuilder csv)
        {
            string scenePath = "Assets/PSXRacing/Scenes/" + def.id + ".unity";
            if (!System.IO.File.Exists(scenePath)) { log.AppendLine(def.id + ": no scene"); return; }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var path = Object.FindFirstObjectByType<TrackPath>();
            if (path == null || path.Count == 0) { log.AppendLine(def.id + ": no TrackPath"); return; }

            float roadHalf = path.roadWidth * 0.5f;
            float lap = Mathf.Max(def.LengthM, 1f);
            int sections = 0, walled = 0, pockets = 0, open = 0, openBridge = 0, bridgeSides = 0;
            int faceSections = 0, face1Sections = 0;
            var lipHist = new int[7];   // lip at 1 m: <2.5 cm, <5, <10, <20, <35, <60, deeper
            float worstStep = 0f; string worstStepAt = "";
            var barrierKinds = new Dictionary<string, int>();
            var openRuns = new List<string>();
            var pocketRuns = new List<string>();
            int[] openStart = { -1, -1 }, pocketStart = { -1, -1 };
            int[] openLast = { -1, -1 }, pocketLast = { -1, -1 };
            bool[] openRunBridge = new bool[2];
            bool[] pocketRunBridge = new bool[2];

            for (int i = 0; i < path.Count; i += Every)
            {
                Vector3 c = path.GetPoint(i);
                Vector3 right = Vector3.Cross(Vector3.up, path.GetTangent(i)).normalized;
                float s = i * path.spacing;
                float bridge = TrackCatalog.BridgeBlend(def, Mathf.Repeat(s, lap));
                for (int si = 0; si < 2; si++)
                {
                    float side = si == 0 ? -1f : 1f;
                    Vector3 o = right * side;
                    if (!Surface(c + o * (roadHalf - 0.3f) + Vector3.up * 3f, 6f, out float tarmacY, out _, out _))
                        continue;
                    sections++;
                    if (bridge > 0.5f) bridgeSides++;

                    // the first wall-like face outward
                    float barrierD = float.PositiveInfinity; string barrierName = "";
                    foreach (float h in RoadsideRules.BarrierRayHeights)
                    {
                        var from = new Vector3(c.x, tarmacY + h, c.z);
                        if (FirstFace(from, o, roadHalf + 14f, out float d, out string nm) && d < barrierD)
                        { barrierD = d; barrierName = nm; }
                    }
                    bool hasBarrier = !float.IsPositiveInfinity(barrierD);
                    if (hasBarrier && barrierD <= roadHalf + OpenReach)
                    {
                        walled++;
                        string kind = Kind(barrierName);
                        barrierKinds.TryGetValue(kind, out int k); barrierKinds[kind] = k + 1;
                    }

                    // the lip, out to the barrier
                    var lips = new float[LipAt.Length];
                    bool face1 = false;
                    for (int li = 0; li < LipAt.Length; li++)
                    {
                        float d = roadHalf + LipAt[li];
                        if (hasBarrier && d > barrierD - 0.15f) { lips[li] = float.NaN; continue; }
                        bool got = Surface(c + o * d + Vector3.up * 3f, 30f, out float y, out _, out bool isFace);
                        lips[li] = got ? tarmacY - y : float.NaN;
                        if (got && isFace && LipAt[li] == 1f) face1 = true;
                    }
                    // steepest step up walking in from 3 m past the edge (or the barrier)
                    float maxStep = 0f; float prev = float.NaN;
                    int faceHits = 0;
                    float startD = roadHalf + 3f;
                    if (hasBarrier) startD = Mathf.Min(startD, barrierD - 0.2f);
                    for (float d = startD; d >= roadHalf - 0.5f; d -= 0.1f)
                    {
                        if (!Surface(c + o * d + Vector3.up * 3f, 30f, out float y, out _, out bool isFace)) { prev = float.NaN; continue; }
                        if (isFace) faceHits++;
                        if (!float.IsNaN(prev)) maxStep = Mathf.Max(maxStep, y - prev);
                        prev = y;
                    }
                    if (faceHits > 0) faceSections++;
                    if (face1) face1Sections++;
                    if (maxStep > worstStep) { worstStep = maxStep; worstStepAt = "wp " + i + (side < 0 ? " L" : " R"); }
                    float l1 = lips[2];
                    if (!float.IsNaN(l1))
                        lipHist[l1 < 0.025f ? 0 : l1 < 0.05f ? 1 : l1 < 0.10f ? 2 : l1 < 0.20f ? 3 : l1 < 0.35f ? 4 : l1 < 0.60f ? 5 : 6]++;

                    // a pocket: somewhere to stand behind the barrier
                    bool pocket = false;
                    if (hasBarrier && barrierD <= roadHalf + 8f)
                        foreach (float beyond in new[] { RoadsideRules.PocketBehindM, 3f })
                            // standable: a face behind the barrier is not somewhere to stand
                            if (Surface(c + o * (barrierD + beyond) + Vector3.up * 4f, 10f, out float py, out _, out bool steep) &&
                                !steep && Mathf.Abs(py - tarmacY) <= PocketBand) { pocket = true; break; }
                    if (pocket) pockets++;

                    // an open edge over a drop
                    bool isOpen = false;
                    if (!hasBarrier || barrierD > roadHalf + OpenReach)
                    {
                        // From the TARMAC, not the waypoint plane 0.12 m under
                        // it, so "within OpenDrop below the tarmac" is exactly that.
                        Vector3 p15 = c + o * (roadHalf + 1.5f), p3 = c + o * (roadHalf + 3f);
                        bool land15 = Surface(new Vector3(p15.x, tarmacY + 3f, p15.z), 3f + OpenDrop, out _, out _, out _);
                        bool land3 = Surface(new Vector3(p3.x, tarmacY + 3f, p3.z), 3f + OpenDrop, out _, out _, out _);
                        isOpen = !land15 || !land3;
                    }
                    if (isOpen) { open++; if (bridge > 0.02f) openBridge++; }

                    Track(ref openStart[si], ref openLast[si], isOpen, i, openRuns, side, path.spacing, bridge > 0.02f, ref openRunBridge[si]);
                    Track(ref pocketStart[si], ref pocketLast[si], pocket, i, pocketRuns, side, path.spacing, bridge > 0.02f, ref pocketRunBridge[si]);

                    csv.Append(def.id).Append(',').Append(i).Append(',').Append(side < 0 ? "L" : "R").Append(',')
                       .Append(s.ToString("0")).Append(',').Append(bridge.ToString("0.00")).Append(',')
                       .Append(tarmacY.ToString("0.000"));
                    foreach (var l in lips) csv.Append(',').Append(float.IsNaN(l) ? "" : l.ToString("0.000"));
                    csv.Append(',').Append(face1 ? 1 : 0).Append(',').Append(faceHits);
                    csv.Append(',').Append(maxStep.ToString("0.000")).Append(',')
                       .Append(hasBarrier ? (barrierD - roadHalf).ToString("0.00") : "").Append(',')
                       .Append(barrierName.Replace(',', ';')).Append(',')
                       .Append(pocket ? 1 : 0).Append(',').Append(isOpen ? 1 : 0).Append('\n');
                }
            }
            for (int si = 0; si < 2; si++)
            {
                Flush(ref openStart[si], ref openLast[si], openRuns, si == 0 ? -1f : 1f, path.spacing, openRunBridge[si]);
                Flush(ref pocketStart[si], ref pocketLast[si], pocketRuns, si == 0 ? -1f : 1f, path.spacing, pocketRunBridge[si]);
            }

            log.AppendLine("=== " + def.id + " (road " + path.roadWidth.ToString("0.0") + " m, " + sections + " half-sections every " + (Every * path.spacing) + " m) ===");
            int lipN = 0; foreach (var h in lipHist) lipN += h;
            string[] names = { "<2.5cm", "2.5-5", "5-10", "10-20", "20-35", "35-60", ">60" };
            var sb = new StringBuilder("  surface 1 m past the tarmac edge sits below the tarmac by:");
            for (int k = 0; k < lipHist.Length; k++)
                sb.Append("  ").Append(names[k]).Append(' ').Append(lipN > 0 ? (100f * lipHist[k] / lipN).ToString("0") : "0").Append('%');
            sb.Append("  (" + lipN + " measured)");
            log.AppendLine(sb.ToString());
            log.AppendLine("  steepest step up per 10 cm walking in from 3 m: " + worstStep.ToString("0.00") + " m at " + worstStepAt);
            log.AppendLine("  FACES (a probe landed on a surface with normal.y < " + FaceNormalY + "): " + faceSections +
                           " half-sections walking in, " + face1Sections + " with the 1 m lip itself on a face");
            var kinds = new StringBuilder();
            foreach (var kv in barrierKinds) kinds.Append(' ').Append(kv.Key).Append('=').Append(kv.Value);
            log.AppendLine("  barrier within " + OpenReach + " m of the edge on " + walled + " of " + sections + " (" + (sections > 0 ? 100f * walled / sections : 0f).ToString("0") + "%):" + kinds);
            log.AppendLine("  POCKETS (standable ground behind the barrier): " + pockets + " half-sections in " + pocketRuns.Count + " runs");
            foreach (var r in Top(pocketRuns, 8)) log.AppendLine("    " + r);
            log.AppendLine("  OPEN EDGES over a " + OpenDrop + " m drop, no barrier: " + open + " half-sections (" + openBridge + " on or near a bridge, of " + bridgeSides + " bridge half-sections) in " + openRuns.Count + " runs");
            foreach (var r in Top(openRuns, 12)) log.AppendLine("    " + r);
        }

        static IEnumerable<string> Top(List<string> runs, int n)
        {
            var sorted = new List<string>(runs);
            sorted.Sort((a, b) => Len(b).CompareTo(Len(a)));
            for (int k = 0; k < Mathf.Min(n, sorted.Count); k++) yield return sorted[k];
        }
        static float Len(string r) { int i = r.IndexOf(" m "); return i > 0 && float.TryParse(r.Substring(0, i), out float v) ? v : 0f; }

        static void Track(ref int start, ref int last, bool on, int i, List<string> runs,
                          float side, float spacing, bool bridge, ref bool runBridge)
        {
            if (on)
            {
                if (start < 0) { start = i; runBridge = false; }
                last = i;
                runBridge |= bridge;
            }
            else if (start >= 0) Flush(ref start, ref last, runs, side, spacing, runBridge);
        }

        static void Flush(ref int start, ref int last, List<string> runs, float side, float spacing, bool bridge)
        {
            if (start < 0) return;
            float len = (last - start + Every) * spacing;
            runs.Add(len.ToString("0") + " m " + (side < 0 ? "left" : "right") + " from wp " + start + " to " + last +
                     " (s " + (start * spacing).ToString("0") + "-" + (last * spacing).ToString("0") + " m)" + (bridge ? " BRIDGE" : ""));
            start = -1; last = -1;
        }

        static string Kind(string name)
        {
            int slash = name.LastIndexOf('/');
            string leaf = slash >= 0 ? name.Substring(slash + 1) : name;
            var sb = new StringBuilder();
            foreach (char ch in leaf) { if (char.IsDigit(ch) || ch == '_' || ch == ' ' || ch == '(') break; sb.Append(ch); }
            return sb.Length > 0 ? sb.ToString() : leaf;
        }

        /// <summary>The highest CONCAVE mesh surface under a point (road, ground,
        /// deck, verge, kerb, shoulder batter): what a wheel rests on or runs
        /// into. <paramref name="face"/> says the hit is steeper than a floor
        /// (normal.y under <see cref="FaceNormalY"/>) — reported, not skipped.
        /// </summary>
        static bool Surface(Vector3 from, float reach, out float y, out Collider on, out bool face)
        {
            y = 0f; on = null; face = false; bool found = false;
            foreach (var h in Physics.RaycastAll(from, Vector3.down, reach, ~0, QueryTriggerInteraction.Ignore))
            {
                var mc = h.collider as MeshCollider;
                if (mc == null || mc.convex) continue;
                if (!found || h.point.y > y)
                {
                    y = h.point.y; on = h.collider; face = h.normal.y < FaceNormalY; found = true;
                }
            }
            return found;
        }

        /// <summary>First wall-like face along a horizontal ray: any non-trigger,
        /// non-car collider whose hit normal is not a floor.</summary>
        static bool FirstFace(Vector3 from, Vector3 dir, float reach, out float dist, out string name)
        {
            dist = float.PositiveInfinity; name = "";
            foreach (var h in Physics.RaycastAll(from, dir, reach, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.gameObject.layer == 2 || h.collider.GetComponentInParent<CarController>() != null) continue;
                if (Mathf.Abs(h.normal.y) > WallNormalY) continue;
                if (h.distance < dist) { dist = h.distance; name = Path(h.collider.transform); }
            }
            return !float.IsPositiveInfinity(dist);
        }

        static string Path(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (int k = 0; k < 2 && t.parent != null; k++) { t = t.parent; sb.Insert(0, t.name + "/"); }
            return sb.ToString();
        }
    }
}
