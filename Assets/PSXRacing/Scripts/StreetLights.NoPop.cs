using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// NO LIGHT POPS (the lightpop round, 2026-11-22). The owner, after PC52:
    /// "now I constantly see light pop-in. Streetlights, headlights. It is very
    /// offputting and distracting while driving or watching replays".
    ///
    /// What popped here: the table of twelve was a HELD SET of lamps chosen
    /// from the view frustum and eased in and out on a quarter-second TIMER
    /// (<see cref="EaseRate"/>). Every swing of the camera brought a lamp into
    /// the frustum (or put one out of it) and every time the eighth-nearest
    /// lamp changed, a pool - and, on the damp or wet night road, its long
    /// glint - came on or went off over a quarter of a second: a fade the eye
    /// reads as a switch. A replay cut snapped it right, but a turn of less
    /// than 45 degrees in one frame did not.
    ///
    /// Now the table is a pure function of the eye, the same in play and in
    /// edit mode, with no state and no timer:
    ///   * each lit lamp within <see cref="MaxReach"/> gets a SCORE, its
    ///     distance times a view factor that grows smoothly from 1 (its pool
    ///     touches the line of sight) to 1 + <see cref="ViewPenalty"/> (right
    ///     behind the eye) - continuous in the eye's pose, so turning moves the
    ///     scores smoothly instead of moving lamps in and out of a frustum;
    ///   * the slots go to the lowest scores; the first score NOT taken is the
    ///     CUT, and each taken lamp's weight is
    ///     DistanceFade(d) x clamp01((cut - score) / <see cref="RankBandM"/>).
    ///     The k-th lowest of continuous scores is itself continuous, so a lamp
    ///     that leaves the table has already faded to nothing, and the one
    ///     coming in starts from nothing, and both move only as fast as the eye
    ///     does: a ramp with distance, never with a clock;
    ///   * the cut never lies further out than <see cref="ScoreCap"/> (the
    ///     reach), so a lamp coming into reach cannot move it in a step.
    /// A camera cut needs nothing: the first frame of the new shot is already
    /// the table that shot wants. PSX_LIGHT_NOPOP=0: the held set and its
    /// timer, as before.
    /// </summary>
    public static partial class StreetLights
    {
        /// <summary>PSX_LIGHT_NOPOP=0: today's light hand-overs (the A/B).
        /// One switch for the lamp table, the lamp field's fade, the headlight
        /// table and the lens births (CarLights).</summary>
        public static bool NoPop = System.Environment.GetEnvironmentVariable("PSX_LIGHT_NOPOP") != "0";

        /// <summary>A lamp straight behind the eye scores its distance times
        /// 1 + this; one whose pool touches the line of sight, times 1.</summary>
        public const float ViewPenalty = 2f;
        /// <summary>Score metres inside the cut over which a lamp comes up.</summary>
        public const float RankBandM = 35f;
        /// <summary>No cut lies further out than this score: the reach.</summary>
        public const float ScoreCap = MaxReach;

        // the scored candidates, one past each share (the first excluded is the cut)
        static readonly int[] sIdx = new int[StreetCapPC + 1];
        static readonly float[] sScore = new float[StreetCapPC + 1];
        static readonly float[] sDist = new float[StreetCapPC + 1];
        static readonly int[] pIdx = new int[PointCap + 1];
        static readonly float[] pScore = new float[PointCap + 1];
        static readonly float[] pDist = new float[PointCap + 1];
        static int nScoredS, nScoredP;
        static float cutS = ScoreCap, cutP = ScoreCap;

        // the table the driver's eye chose this frame: registry handles and
        // their rank weights (the part of the weight that is not the lamp's
        // own distance), for every other camera to draw with
        static readonly int[] chosenHandle = new int[MaxLamps];
        static readonly float[] chosenRank = new float[MaxLamps];
        static int chosenCount;

        /// <summary>The view factor (1 .. 1 + ViewPenalty) of a lamp at offset
        /// <paramref name="d"/> (distance <paramref name="dist"/>) whose light
        /// reaches <paramref name="radius"/>, for a unit forward.</summary>
        internal static float ViewFactor(Vector3 d, float dist, float radius, Vector3 fwd)
        {
            if (dist < 1e-3f) return 1f;
            float c = Mathf.Clamp(Vector3.Dot(d, fwd) / dist, -1f, 1f);
            float sa = Mathf.Min(1f, radius / dist), ca = Mathf.Sqrt(Mathf.Max(0f, 1f - sa * sa));
            // cos(angle off the axis less the pool's own angular radius), 1 inside it
            float ce = c >= ca ? 1f : c * ca + Mathf.Sqrt(Mathf.Max(0f, 1f - c * c)) * sa;
            return 1f + ViewPenalty * 0.5f * (1f - ce);
        }

        /// <summary>The scored ranking for an eye (unit forward): the lowest
        /// scores of each kind into sIdx / pIdx, and each kind's cut.</summary>
        static void RankScored(Vector3 eye, Vector3 fwd)
        {
            nScoredS = nScoredP = 0;
            int capS = StreetCapNow;
            float reach2 = MaxReach * MaxReach;
            bool streetOn = StreetOn;
            for (int i = 0; i < high; i++)
            {
                ref Entry e = ref entries[i];
                if (!e.used) continue;
                if (e.owned && e.owner == null) { Free(i); continue; }
                if (!Lit(ref e, streetOn)) continue;
                Vector3 d = e.pos - eye;
                float d2 = d.sqrMagnitude;
                if (d2 > reach2) continue;
                float dist = Mathf.Sqrt(d2);
                // a tail lamp (four at most, the cars round the eye) by its
                // distance alone: a car racing past a replay's bystander must
                // not swing its own red pool out of the table as it passes
                float s = e.kind == Kind.Street ? dist * ViewFactor(d, dist, e.radius, fwd) : dist;
                if (s >= ScoreCap) continue;
                if (e.kind == Kind.Street) OfferScored(sIdx, sScore, sDist, ref nScoredS, i, s, dist, capS + 1);
                else OfferScored(pIdx, pScore, pDist, ref nScoredP, i, s, dist, PointCap + 1);
            }
            cutS = nScoredS > capS ? Mathf.Min(sScore[capS], ScoreCap) : ScoreCap;
            cutP = nScoredP > PointCap ? Mathf.Min(pScore[PointCap], ScoreCap) : ScoreCap;
            if (nScoredS > capS) nScoredS = capS;
            if (nScoredP > PointCap) nScoredP = PointCap;
            // the choice, by handle, with its rank weight
            chosenCount = 0;
            for (int k = 0; k < nScoredP; k++) Choose(pIdx[k], RankWeight(cutP, pScore[k]));
            for (int k = 0; k < nScoredS; k++) Choose(sIdx[k], RankWeight(cutS, sScore[k]));
        }

        static float RankWeight(float cut, float s) => Mathf.Clamp01((cut - s) / RankBandM);

        static void Choose(int slot, float rank)
        {
            if (rank <= 0f || chosenCount >= MaxLamps) return;
            chosenHandle[chosenCount] = (entries[slot].gen << 16) | slot;
            chosenRank[chosenCount] = rank;
            chosenCount++;
        }

        static void OfferScored(int[] idx, float[] score, float[] dist, ref int held, int i, float s, float dd, int cap)
        {
            if (held == cap && s >= score[cap - 1]) return;
            int k = held < cap ? held++ : cap - 1;
            while (k > 0 && score[k - 1] > s)
            {
                score[k] = score[k - 1]; idx[k] = idx[k - 1]; dist[k] = dist[k - 1];
                k--;
            }
            score[k] = s; idx[k] = i; dist[k] = dd;
        }

        /// <summary>The chosen table, each lamp at its rank weight times its
        /// distance fade from THIS eye, at its live position.</summary>
        static void EmitScored(Vector3 eye)
        {
            int n = 0;
            bool streetOn = StreetOn;
            MeterBegin();
            for (int h = 0; h < chosenCount; h++)
            {
                if (!Valid(chosenHandle[h], out int slot)) continue;
                ref Entry e = ref entries[slot];
                if (e.owned && e.owner == null) continue;
                if (!Lit(ref e, streetOn)) continue;
                MeterNote(chosenHandle[h], chosenRank[h]);
                Emit(ref n, slot, Mathf.Min(chosenRank[h], DistanceFade((e.pos - eye).magnitude)));
            }
            Finish(n);
        }

        // ------------------------------------------------------------------
        //  THE LIGHT-POP METER's hooks (Dev/LightPopMeter.cs): what the
        //  driver's eye drew, by lamp, as the part of each lamp's weight that
        //  is NOT its own distance fade; and a re-draw of the table at given
        //  weights (the frame before's) with today's positions and intensities.
        // ------------------------------------------------------------------
        /// <summary>The meter is measuring: no camera pushes the lamp table
        /// (it pushes its own state for the frame it renders).</summary>
        internal static bool MeterHold;
        internal static bool MeterOn;
        internal static readonly System.Collections.Generic.Dictionary<int, float> MeterRank = new System.Collections.Generic.Dictionary<int, float>();
        static bool meterRecording;

        static void MeterBegin()
        {
            meterRecording = MeterOn && !MeterHold && meterDriverEmit;
            if (meterRecording) MeterRank.Clear();
        }
        static void MeterNote(int handle, float rank) { if (meterRecording) MeterRank[handle] = rank; }
        static bool meterDriverEmit;

        /// <summary>Push the table at the given rank weights (handle to
        /// weight), each times its distance fade from <paramref name="eye"/>.</summary>
        internal static void MeterEmit(System.Collections.Generic.Dictionary<int, float> ranks, Vector3 eye)
        {
            int n = 0;
            bool streetOn = StreetOn;
            foreach (var kv in ranks)
            {
                if (!Valid(kv.Key, out int slot)) continue;
                ref Entry e = ref entries[slot];
                if (e.owned && e.owner == null) continue;
                if (!Lit(ref e, streetOn)) continue;
                Emit(ref n, slot, NoPop ? Mathf.Min(kv.Value, DistanceFade((e.pos - eye).magnitude)) : kv.Value * DistanceFade((e.pos - eye).magnitude));
            }
            Finish(n);
        }

        /// <summary>A registered lamp's position, kind and nominal (sRGB-free)
        /// strength, for the meter's report.</summary>
        internal static bool MeterLamp(int handle, out Vector3 pos, out Kind kind, out bool field)
        {
            pos = default; kind = Kind.Street; field = false;
            if (!Valid(handle, out int slot)) return false;
            pos = entries[slot].pos; kind = entries[slot].kind; field = entries[slot].field;
            return true;
        }

        /// <summary>The field as last pushed (the meter's frame-before state).</summary>
        internal static Texture MeterFieldTex => fieldTex;
        internal static string MeterFieldState => "lamps " + FieldLamps + " v" + builtVersion + "/" + fieldVersion + " base " + builtBaseY.ToString("0") + " at " + builtOrigin.x.ToString("0") + "," + builtOrigin.y.ToString("0");
        internal static Vector4 MeterFieldST => fieldTex != null && FieldActive ? new Vector4(builtOrigin.x, builtOrigin.y, 1f / (FieldRes * FieldTexelM), 1f) : Vector4.zero;
    }
}
