using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// HOW A ROAD MEETS THE GROUND, AND WHEN IT GETS A BARRIER. One table, read
    /// by every builder (circuits, stages, Charlotte, the town) and every audit
    /// that polices them, so the rule cannot drift between the thing that builds
    /// a roadside and the thing that measures it.
    ///
    /// The owner, 2026-09-13, after "it is difficult to drive back onto tracks,
    /// especially mountain tracks because of their walls; the thick roads stick
    /// out of the ground; most roads aren't more than an inch above the shoulder
    /// dirt; all sections of bridges should have walls": **"Roads sitting cm
    /// above the ground do not need rails/walls, they should meet the ground
    /// properly by DOT standards."**
    ///
    /// So this is FHWA / AASHTO Roadside Design Guide practice, fitted to this
    /// car:
    ///
    ///   * the surface beside the tarmac is flush — an edge drop of an inch, and
    ///     a hard ceiling of two — and it carries a collider, so what a wheel
    ///     and the body box meet is the shoulder, never a slab face;
    ///   * beyond it the land falls or rises at a slope a car can recover on:
    ///     1V:6H inside the clear zone, 1V:4H at the steepest recoverable, 1V:3H
    ///     traversable only with a flat runout at its toe;
    ///   * a BARRIER only where one is warranted: every structure edge (bridge
    ///     rail, carried past each abutment as approach rail), a critical slope
    ///     or drop that grading cannot make recoverable, and medians. Never on a
    ///     level or graded verge, and never to hide a road that stands proud.
    ///
    /// The coarse ground lattice may stay sunk under an exact shoulder surface
    /// (that is what keeps it from clipping through the tarmac); it is the
    /// shoulder surface and its collider that meet the road, not the lattice.
    ///
    /// Why the numbers fit the car: the player's body box sits 0.19-0.22 m over
    /// the road on a stock FD and 0.08-0.12 m at the lowest ride height, and it
    /// overhangs every wheel ray, so any face taller than the box's clearance
    /// is a wall at every speed. A 2.5 cm lip clears every shell at every
    /// setup; 1V:6H (9.5 degrees) is under every stock approach angle and
    /// inside the FWD-in-snow climb limit (11.7 degrees).
    /// </summary>
    public static class RoadsideRules
    {
        // ------------------------------------------------------------------
        //  The edge
        // ------------------------------------------------------------------
        /// <summary>Target drop from the tarmac (or kerb strip) surface to the
        /// shoulder surface beside it: the owner's inch.</summary>
        public const float EdgeDropM = 0.025f;
        /// <summary>Hard ceiling on that drop anywhere a car can reach the
        /// edge. The audits FAIL past it.</summary>
        public const float EdgeDropFailM = 0.05f;
        /// <summary>Cross-fall of a shoulder away from the road (4%).</summary>
        public const float ShoulderCrossFall = 0.04f;

        // ------------------------------------------------------------------
        //  Slopes, as rise over run (positive numbers)
        // ------------------------------------------------------------------
        /// <summary>1V:6H — the recoverable foreslope a clear zone is graded to.</summary>
        public const float RecoverableSlope = 1f / 6f;
        /// <summary>1V:4H — the steepest slope still called recoverable.</summary>
        public const float SteepestRecoverableSlope = 1f / 4f;
        /// <summary>1V:3H — traversable but not recoverable; steeper is critical.</summary>
        public const float TraversableSlope = 1f / 3f;
        /// <summary>A ditch's backslope where a cut is graded rather than faced.</summary>
        public const float BackSlope = 1f / 3f;

        /// <summary>Metres past the shoulder a stage keeps recoverable (1V:6H)
        /// before it may steepen to 1V:4H. RDG gives about 3-3.7 m for a low-
        /// volume 45-50 mph road; this game drives faster and has no traffic.</summary>
        public const float ClearZoneM = 3.5f;

        // ------------------------------------------------------------------
        //  Keeping the coarse lattice under the exact shoulder
        // ------------------------------------------------------------------
        /// <summary>How far a circuit/stage ground-lattice vertex under a
        /// shoulder surface is held below that surface's design height.</summary>
        public const float HideMarginM = 0.15f;
        /// <summary>The same for Charlotte's 8 m lattice.</summary>
        public const float CityHideMarginM = 0.10f;
        /// <summary>How far below the finished lattice a shoulder surface's
        /// outer toe is tucked, so the two cross instead of abutting: a car rides
        /// the higher of two continuous surfaces, which is continuous.</summary>
        public const float ToeTuckM = 0.10f;
        /// <summary>Over how many metres the toe tucks.</summary>
        public const float ToeTuckRunM = 0.4f;

        // ------------------------------------------------------------------
        //  Barrier warrant
        // ------------------------------------------------------------------
        /// <summary>A roadside is CRITICAL — a barrier is warranted — when,
        /// within <see cref="WarrantReachM"/> of the shoulder, the land falls
        /// more than this at a slope steeper than <see cref="TraversableSlope"/>.
        /// </summary>
        public const float CriticalFallM = 1.5f;
        /// <summary>How far past the shoulder the warrant looks.</summary>
        public const float WarrantReachM = 5f;
        /// <summary>Stations (4 m each) a bridge's wall runs on past each deck
        /// end, onto the approach, before the graded roadside may take over.</summary>
        public const int ApproachRailStations = 8;
        /// <summary>A warranted barrier run's end flares away from the road
        /// at this ratio (lateral over longitudinal) over its last chords and
        /// is buried into the slope, the way a guardrail terminal is.</summary>
        public const float EndFlareRatio = 1f / 15f;
        public const int EndFlareStations = 3;

        /// <summary>Is a fall of <paramref name="fallM"/> over a horizontal run
        /// of <paramref name="runM"/> beside the shoulder critical?</summary>
        public static bool IsCriticalFall(float fallM, float runM) =>
            fallM > CriticalFallM && (runM <= 0.01f || fallM / runM > TraversableSlope);

        // ------------------------------------------------------------------
        //  Audit thresholds (the audits read these; the builders aim under them)
        // ------------------------------------------------------------------
        /// <summary>A face that rises more than this within <see cref="FaceRunM"/>
        /// of horizontal travel is a wall to the body box at the lowest ride
        /// height (keeps 2-3 cm of the 8-9 cm clearance).</summary>
        public const float FaceRiseFailM = 0.06f;
        public const float FaceRunM = 0.13f;
        /// <summary>A slope steeper than 1V:4H sustained over this many metres
        /// inside the clear zone fails; steeper than 1V:6H warns.</summary>
        public const float SlopeSustainM = 1.0f;
        /// <summary>A barrier with drivable land within this of road height
        /// <see cref="PocketBehindM"/> behind it is an UNWARRANTED barrier (a
        /// pocket a car can be trapped in).</summary>
        public const float PocketBandM = 0.3f;
        public const float PocketBehindM = 1.5f;
        /// <summary>An unguarded structure/edge over a drop deeper than this is
        /// a fall-off.</summary>
        public const float OpenDropM = 1.0f;
        /// <summary>A gap along a deck edge's barrier longer than this fails.</summary>
        public const float DeckRailGapFailM = 0.5f;
        /// <summary>Heights above the tarmac at which a barrier is looked for.</summary>
        public static readonly float[] BarrierRayHeights = { 0.35f, 0.8f };
        /// <summary>Lowest body-box clearance any player setup reaches.</summary>
        public const float CarClearanceFloorM = 0.08f;

        // ------------------------------------------------------------------
        //  The warrant, walked across a section
        // ------------------------------------------------------------------
        /// <summary>
        /// THE ONE WALK THE BUILDER AND THE AUDIT BOTH TAKE to ask whether a
        /// cross-section warrants a barrier. <paramref name="y"/> is the surface
        /// a car beside the road would be on, sampled every <paramref
        /// name="pitch"/> metres outward (NaN where nothing is under it);
        /// every pair of samples from <paramref name="from"/> to <paramref
        /// name="to"/> with the outer one lower is put to <see
        /// cref="IsCriticalFall"/>, and the deepest critical fall is returned
        /// (0 for none, +infinity where the walk reaches a void past solid
        /// ground), with the index of its foot.
        ///
        /// Why a walk and not one sample: the stage plan used to ask a single
        /// question — the land at the warrant reach against the shoulder, over
        /// the whole reach — while the edge audit asked every pair on the
        /// section it measured. A fill that drops 1.6 m in the last three
        /// metres is critical to the second and not to the first, and that —
        /// with the lattice sagging under the DEM the plan read — is what "a
        /// barrier that ends one station short of a critical fall" was at eight
        /// run ends on three mountains (2026-09-13). The convention the
        /// callers share: sample 0 is the tarmac edge, and the walk ends at the
        /// kerb strip's outer edge plus <see cref="WarrantReachM"/>.
        ///
        /// <paramref name="slackM"/> is added to every fall before it is
        /// judged: the audit passes 0; a builder reading a lattice it has not
        /// built yet passes a few centimetres, so the thing that builds a wall
        /// errs toward the stricter side of the thing that measures it.
        /// </summary>
        public static float WorstCriticalFall(float[] y, int from, int to, float pitch, float slackM, out int footK)
        {
            footK = -1;
            float worst = 0f, prefixMax = float.NegativeInfinity;
            for (int b = from; b <= to && b < y.Length; b++)
            {
                if (float.IsNaN(y[b]))
                {
                    if (prefixMax > float.NegativeInfinity) { worst = float.PositiveInfinity; footK = b; }
                    break;
                }
                if (prefixMax - y[b] + slackM > CriticalFallM)
                {
                    for (int a = from; a < b; a++)
                    {
                        float fall = y[a] - y[b];
                        if (fall <= worst || !IsCriticalFall(fall + slackM, (b - a) * pitch)) continue;
                        worst = fall;
                        footK = b;
                    }
                }
                prefixMax = Mathf.Max(prefixMax, y[b]);
            }
            return worst;
        }

        /// <summary>The height over a lane a car's body occupies, with
        /// margin. Anything solid lower than this over a lane is a wall in
        /// the road (the city audit's lane survey); and two ribbons whose
        /// surfaces are closer than this plus a deck's depth cannot pass one
        /// over the other, so they may not overlap in plan either — the city
        /// builder splits the space between them (a 1.1 m step used to let
        /// I-277's two decks overlap, with their rails standing in lanes).</summary>
        public const float CarBandM = 2.0f;

        // ------------------------------------------------------------------
        //  Where a carried slope meets the coarse lattice
        // ------------------------------------------------------------------
        /// <summary>The least a ground lattice may sit under a shoulder
        /// surface before it is "one crest away from showing through" (the
        /// terrain audit's floor). The stage lattice solve reads it too: a
        /// carried slope that comes this close to the lattice anywhere but in
        /// its run-in to a catch has grazed it, and the lattice is held under
        /// it there, so the builder and the audit agree on what a catch is.
        /// </summary>
        public const float LatticeUnderMinM = 0.03f;
        /// <summary>How far inward of a shoulder's toe the lattice may run
        /// within <see cref="LatticeUnderMinM"/> of it as the designed
        /// crossing into the tuck (the terrain audit excuses it; the stage
        /// solve leaves a carry's run-in to its catch inside it).</summary>
        public const float ToeCrossingMaxM = 2.0f;
        /// <summary>Horizontal run a foreslope grade is measured over, for
        /// <see cref="SlopeSustainM"/>: the edge audit's window, and the one
        /// the stage solve reads a clear zone's lattice with.</summary>
        public const float SlopeWindowM = 0.25f;
    }
}
