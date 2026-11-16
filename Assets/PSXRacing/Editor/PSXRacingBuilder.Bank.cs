using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE ROAD IS BANKED AND CROWNED, AS THE REAL ONE IS.
    ///
    /// The owner, 2026-11-16: "Did you catch the banking of the roads? I've
    /// noticed many of the mountain road tracks are very flat. They have
    /// turns, but almost no (if any) banking". He was right: every stage
    /// ring was flat across (BuildRoad: "The ring is flat across"), while the
    /// 1 m lidar under the real roads shows them crowned about 2% on the
    /// straights and superelevated 6-10% in the bends, up to 12% on the
    /// switchbacks (tools/roads/bake_bank.mjs measures it and writes
    /// Art/(stage)/(prefix)_bank.json: per waypoint, the left and right tarmac
    /// edge's height over the centre line as a cross slope).
    ///
    /// THE CROSS-SECTION. The centre line keeps its height (the bakes read the
    /// lidar there) and the road turns about it, the way a two-lane road is
    /// superelevated: each half is a plane from the centre to its edge, so a
    /// crown is two planes falling away and a bank one plane through both. The
    /// road ring's three vertices (left edge, centre, right edge) draw both
    /// exactly. EVERYTHING BUILT FROM AN EDGE stands on that edge: the kerb
    /// strip, the shoulder section and its toe, the walls, rails and posts, the
    /// cut and fill sections and the lattice held under them, the rock tops,
    /// the pads, the decks and parapets, the tunnel tube. So on each side of
    /// the road a station's roadside is the flat road's, lifted by that side's
    /// edge (<see cref="BankEdge"/>); inside the tarmac the lift is linear from
    /// the centre (<see cref="BankAcross"/>). The ground field (Refine's
    /// RoadFoot.roadY) reads the same surface, so the dig under the pavement
    /// and the graded roadside follow the bank without a second rule.
    ///
    /// A ROUTE'S ENDS are faded to flat over <see cref="BankEndFadeStations"/>
    /// (the end pads carry a flat road on past the last ring), before the
    /// start and finish lines on every stage.
    ///
    /// PSX_STAGE_BANK=0 is today's flat road exactly: no file is read and every
    /// helper here answers zero. A stage with no file (the coast, a new bake
    /// the tool has not been run on) is flat too.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        [Serializable]
        class BankFile
        {
            public int schema;
            public int count;
            public float halfWidthM;
            public string unit;
            public int[] left, right;
            public string source;
        }

        /// <summary>Per waypoint: the left and right tarmac edge's height over
        /// the centre line, metres, at THIS road's width. Null = flat.</summary>
        static float[] bankEdgeL, bankEdgeR;

        /// <summary>Stations over which a route with ends fades from its bank
        /// to flat at each end: 32 m, about AASHTO's relative gradient for an
        /// 8% bank on a 3 m half (0.75%). Every stage's start line is further
        /// in than this (33-60 m).</summary>
        internal const int BankEndFadeStations = 8;

        /// <summary>How far a banked deck's end cap stands past its station
        /// line (PSXRacingBuilder.BuildOneDeck).</summary>
        internal const float DeckEndPushM = 0.02f;

        internal static bool StageBankSwitchOn =>
            Environment.GetEnvironmentVariable("PSX_STAGE_BANK") != "0";

        /// <summary>Is this build's road banked at all?</summary>
        internal static bool Banked => bankEdgeL != null;

        static void ClearStageBank() { bankEdgeL = bankEdgeR = null; }

        /// <summary>
        /// Read the stage's bank file for the waypoints just loaded (StageLoadDem
        /// calls this last). A file whose count is not the route's is refused
        /// out loud: a stage re-baked after the bank file was made would
        /// otherwise bank the wrong bends.
        /// </summary>
        static void LoadStageBank(List<Vector3> pts)
        {
            ClearStageBank();
            if (track == null || !track.stage || pts == null || pts.Count < 2) return;
            string rel = StageArtDir + "/" + StagePrefix + "_bank.json";
            if (!StageBankSwitchOn)
            {
                Log("Bank: PSX_STAGE_BANK=0 - the road is flat across (" + rel + " not read).");
                return;
            }
            string path = ProjectRootPath(rel);
            if (!File.Exists(path))
            {
                Log("Bank: no " + rel + " - the road is flat across (run tools/roads/bake_bank.mjs " + StagePrefix + ").");
                return;
            }
            BankFile f;
            try { f = JsonUtility.FromJson<BankFile>(File.ReadAllText(path)); }
            catch (Exception ex) { Log("WARN: Bank: " + rel + " unreadable (" + ex.Message + ") - flat."); return; }
            int n = pts.Count;
            if (f == null || f.left == null || f.right == null || f.left.Length != n || f.right.Length != n)
            {
                Log($"WARN: Bank: {rel} has {(f?.left?.Length ?? -1)} stations against the route's {n} - flat. " +
                    "Re-run tools/roads/bake_bank.mjs " + StagePrefix + " after any re-bake.");
                return;
            }
            float half = RoadWidth * 0.5f;
            bankEdgeL = new float[n];
            bankEdgeR = new float[n];
            for (int i = 0; i < n; i++)
            {
                bankEdgeL[i] = f.left[i] / 1000f * half;
                bankEdgeR[i] = f.right[i] / 1000f * half;
            }
            // The ends of a route fade to flat: the end pad (BuildStageEndPads)
            // carries a flat core 6 m on past the last ring.
            if (!Loop)
                for (int i = 0; i < n; i++)
                {
                    int fromEnd = Mathf.Min(i, n - 1 - i);
                    if (fromEnd >= BankEndFadeStations) continue;
                    float w = Mathf.SmoothStep(0f, 1f, fromEnd / (float)BankEndFadeStations);
                    bankEdgeL[i] *= w;
                    bankEdgeR[i] *= w;
                }
            float maxDy = 0f, maxTilt = 0f;
            int tilted = 0, crowned = 0;
            for (int i = 0; i < n; i++)
            {
                maxDy = Mathf.Max(maxDy, Mathf.Abs(bankEdgeL[i]), Mathf.Abs(bankEdgeR[i]));
                float tilt = (bankEdgeL[i] - bankEdgeR[i]) * 0.5f / half;
                maxTilt = Mathf.Max(maxTilt, Mathf.Abs(tilt));
                if (bankEdgeL[i] * bankEdgeR[i] < 0f) tilted++;
                else if (bankEdgeL[i] < 0f && bankEdgeR[i] < 0f) crowned++;
            }
            Log($"Bank: {rel} - {tilted} of {n} stations banked one way, {crowned} crowned; steepest bank " +
                $"{maxTilt * 100f:0.0}%, an edge up to {maxDy:0.000} m off the centre line (PSX_STAGE_BANK=0 = flat).");
        }

        static int BankIdx(int i)
        {
            int n = bankEdgeL.Length;
            return Loop ? ((i % n) + n) % n : Mathf.Clamp(i, 0, n - 1);
        }

        /// <summary>The tarmac edge's height over the centre line at station
        /// <paramref name="i"/> on <paramref name="side"/> (-1 left, +1 right,
        /// as RightAt points), metres. Everything on that side past the edge
        /// is lifted by this.</summary>
        internal static float BankEdge(int i, float side)
        {
            if (bankEdgeL == null) return 0f;
            int k = BankIdx(i);
            return side < 0f ? bankEdgeL[k] : bankEdgeR[k];
        }

        /// <summary><see cref="BankEdge"/> between stations a and b, t along.</summary>
        internal static float BankEdgeAt(int a, int b, float t, float side) =>
            bankEdgeL == null ? 0f : Mathf.Lerp(BankEdge(a, side), BankEdge(b, side), t);

        /// <summary><see cref="BankEdge"/> at a fractional station (s0 + t).</summary>
        internal static float BankEdgeF(float station, float side)
        {
            if (bankEdgeL == null) return 0f;
            int a = Mathf.FloorToInt(station);
            return BankEdgeAt(a, a + 1, station - a, side);
        }

        /// <summary>The road surface's height over the centre line at
        /// <paramref name="lateral"/> metres from it (+ right): linear from the
        /// centre to the edge, the edge's own beyond it.</summary>
        internal static float BankAcross(int i, float lateral)
        {
            if (bankEdgeL == null) return 0f;
            float half = RoadWidth * 0.5f;
            return BankEdge(i, lateral < 0f ? -1f : 1f) * Mathf.Clamp01(Mathf.Abs(lateral) / Mathf.Max(half, 0.01f));
        }

        /// <summary><see cref="BankAcross"/> between stations a and b, t along.</summary>
        internal static float BankAcrossAt(int a, int b, float t, float lateral) =>
            bankEdgeL == null ? 0f : Mathf.Lerp(BankAcross(a, lateral), BankAcross(b, lateral), t);

        /// <summary>A ribbon ring's edge lift: the station's, or the gap's lerp
        /// on a slice.</summary>
        static float RingBank(in RibbonRing g, float side)
        {
            if (bankEdgeL == null) return 0f;
            if (g.t <= 0f || g.gap < 0) return BankEdge(g.station, side);
            RibbonEnds(bankEdgeL.Length, g.gap, out int a, out int b);
            return BankEdgeAt(a, b, g.t, side);
        }

        /// <summary>The cross-section for the runtime (TrackPath.bankL/bankR:
        /// the AI's corner speeds), as slopes. Null on a flat road, so a scene
        /// built with the switch off carries nothing.</summary>
        static void BakeBankInto(PSXRacing.TrackPath path)
        {
            path.bankL = path.bankR = null;
            if (bankEdgeL == null || path == null || path.waypoints == null || path.waypoints.Length != bankEdgeL.Length) return;
            int n = bankEdgeL.Length;
            float half = Mathf.Max(RoadWidth * 0.5f, 0.01f);
            path.bankL = new float[n];
            path.bankR = new float[n];
            for (int i = 0; i < n; i++)
            {
                path.bankL[i] = bankEdgeL[i] / half;
                path.bankR[i] = bankEdgeR[i] / half;
            }
        }

        /// <summary>One side's edge as slopes per waypoint (the planting
        /// rule's envelope, PlantSolidAudit.BeginForBuilder); null when flat.</summary>
        internal static float[] BankSlopes(float side)
        {
            if (bankEdgeL == null) return null;
            float half = Mathf.Max(RoadWidth * 0.5f, 0.01f);
            var src = side < 0f ? bankEdgeL : bankEdgeR;
            var o = new float[src.Length];
            for (int i = 0; i < src.Length; i++) o[i] = src[i] / half;
            return o;
        }

        /// <summary>The road's surface normal at <paramref name="lateral"/>
        /// metres right of the centre line, between stations a and b: the
        /// up a car standing there is rolled to.</summary>
        internal static Vector3 BankNormalAt(List<Vector3> pts, int a, int b, float t, float lateral)
        {
            Vector3 right = Vector3.Lerp(RightAt(pts, a), RightAt(pts, b), t);
            right.y = 0f;
            right = right.sqrMagnitude > 1e-8f ? right.normalized : Vector3.right;
            if (bankEdgeL == null) return Vector3.up;
            float half = Mathf.Max(RoadWidth * 0.5f, 0.01f);
            // dy per metre going right: the right half rises at its edge's
            // slope, the left half falls toward the centre at its own.
            float slope = lateral >= 0f ? BankEdgeAt(a, b, t, 1f) / half : -BankEdgeAt(a, b, t, -1f) / half;
            return (Vector3.up - right * slope).normalized;
        }

        /// <summary>The tarmac surface's height (lift included) at the edge on
        /// <paramref name="side"/> of station <paramref name="i"/>: what every
        /// roadside piece on that side is measured from.</summary>
        internal static float EdgeRoadY(List<Vector3> pts, int i, float side) =>
            pts[i].y + RoadLift + BankEdge(i, side);
    }
}
