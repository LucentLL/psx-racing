using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The trunks of a forest too big to bake a collider per tree.
    ///
    /// Owner, 2026-09-19: "Trees should also occupy space with their trunks,
    /// stopping a car if it drives into it." A circuit's forty-odd trees each
    /// carry their own capsule. A mountain stage plants twenty to thirty
    /// THOUSAND, merged into chunk meshes, and a baked collider apiece is a
    /// scene of thirty thousand components the phone has to deserialise and
    /// hold for a race that passes within reach of a fraction of them.
    ///
    /// So the builder writes the trunks as a TABLE — base and card width, four
    /// floats a tree, taken from the same position the billboard was planted
    /// at — and this stands real capsules only where a car can reach them:
    /// every trunk in the 3x3 block of cells round each car. A cell is
    /// <see cref="Cell"/> metres, so a collider is always in place at least
    /// that far ahead of the bumper; at 90 m/s and 60 Hz a car covers 1.5 m a
    /// step, and the block moves on a whole cell before the car can get near
    /// its edge. Cells more than two away from every car are torn down again,
    /// so a stage run holds a few hundred capsules at a time, not thousands.
    ///
    /// Solid layer, like every wall: the suspension rays ignore it, so a wheel
    /// never takes spring force off a trunk — the BODY hits it.
    ///
    /// THE SAME EVENING, and the reason for the second half of this file: "I
    /// just drove straight through a tree without impact." The capsules were
    /// there (tools/tree-play-check.ps1 drives the real car at them in the
    /// running game). What was wrong was WHERE, against what is DRAWN:
    ///
    ///   * the pack paints its trunks wherever the photograph had them — up
    ///     to 1.6 m off the middle of the billboard, which is where the two
    ///     cards cross and the capsule stands. Aim at the winter oak's painted
    ///     trunk and the car passes a metre and a half from the real one. The
    ///     atlas composer now slides every billboard until its trunk is under
    ///     the crossing (TreeKit.CentreOnTrunk), in all five season dresses;
    ///   * and a third of the species carry LEAVES DOWN TO THE BUMPER — six
    ///     metres of them on the big orange maple — round a 25 cm capsule.
    ///     Driving through that with nothing happening is driving through a
    ///     tree. Foliage is not a wall, so it is not given a collider; it is
    ///     BRUSH: inside it the car is dragged down hard (see
    ///     <see cref="BrushDrag"/>), the way a hedge takes a car, and the
    ///     trunk is still in the middle of it for whoever keeps going. How far
    ///     the brush reaches is measured off each billboard per SEASON — the
    ///     same cell is a maple in leaf in October and bare sticks in January.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class TreeTrunks : MonoBehaviour
    {
        /// <summary>x, y, z of the trunk's BASE in world space, then w — four
        /// floats per tree, in planting order. w is the CARD'S WIDTH for a
        /// forest table (<see cref="cells"/> filled: the radius comes off the
        /// painted trunk, see <see cref="RadiusOf"/>) and the trunk's RADIUS
        /// for a plain one (<see cref="SetTrunks"/>).</summary>
        [SerializeField] float[] trunks = new float[0];

        /// <summary>Which of the atlas's sixteen cells each tree wears. Empty
        /// for a plain table.</summary>
        [SerializeField] byte[] cells = new byte[0];

        /// <summary>Per cell: the painted trunk's half-width as a fraction of
        /// the card's width — the widest any season's billboard paints it, so
        /// one capsule serves all five.</summary>
        [SerializeField] float[] trunkFrac = new float[0];

        /// <summary>Per DRESS and cell ([dress * 16 + cell]): how far the low
        /// foliage reaches from the trunk at bumper height, as a fraction of
        /// the card's width. 0 where a car meets only the trunk.</summary>
        [SerializeField] float[] brushFrac = new float[0];

        /// <summary>How far up a trunk is solid. Above any car's roof; below
        /// the lowest deck a tree is allowed to stand under (the forest pass
        /// wants a tree's full height plus 3.5 m of air before it plants one
        /// beneath a span).</summary>
        public float trunkHeight = 4f;

        /// <summary>Side of one grid cell, metres. A car always has at least
        /// this much trunk-covered ground in front of it.</summary>
        public const float Cell = 40f;

        /// <summary>Walls, buildings, trunks: the layer CarController keeps
        /// off its suspension mask.</summary>
        public const int SolidLayer = 9;

        public const int CellsPerAtlas = 16;

        /// <summary>A trunk is never thinner than a fence post nor fatter than
        /// an old oak, whatever the billboard's pixels say.</summary>
        public const float MinRadius = 0.22f, MaxRadius = 0.60f;

        /// <summary>Brush past this is scenery, not an obstacle: the drooping
        /// tips of a twelve-metre crown are not what stops a car.</summary>
        public const float MaxBrush = 4.5f;

        /// <summary>What foliage does to a car inside it: a deceleration of
        /// this many (m/s^2) per (m/s), so 1.8 takes 22 m/s down by about
        /// 5 m/s for every three metres of crown — a hedge, not a wall and not
        /// nothing.</summary>
        public const float BrushDrag = 1.8f;

        /// <summary>Half a car, so the brush starts when the BODY reaches the
        /// leaves rather than when its centre does.</summary>
        const float CarHalfWidth = 0.9f;

        public int Count => trunks != null ? trunks.Length / 4 : 0;

        bool IsForest => cells != null && cells.Length == Count && Count > 0;

        public Vector3 BaseOf(int i) => new Vector3(trunks[i * 4], trunks[i * 4 + 1], trunks[i * 4 + 2]);

        /// <summary>The capsule's radius: the painted trunk's, off the card
        /// it is painted on, for a forest; as given for a plain table.</summary>
        public float RadiusOf(int i)
        {
            float w = trunks[i * 4 + 3];
            if (!IsForest) return w;
            int c = cells[i];
            float frac = c < trunkFrac.Length ? trunkFrac[c] : 0.025f;
            return Mathf.Clamp(frac * w, MinRadius, MaxRadius);
        }

        /// <summary>A forest tree's card width, metres; 0 for a plain table.</summary>
        public float CardWidthOf(int i) => IsForest ? trunks[i * 4 + 3] : 0f;

        /// <summary>The atlas cell a forest tree wears; -1 for a plain table.</summary>
        public int AtlasCellOf(int i) => IsForest ? cells[i] : -1;

        /// <summary>How far this tree's low foliage reaches in the given
        /// dress; 0 for a tree a car meets only the trunk of.</summary>
        public float BrushOf(int i, int dress)
        {
            if (!IsForest) return 0f;
            int k = dress * CellsPerAtlas + cells[i];
            if (k < 0 || k >= brushFrac.Length) return 0f;
            float b = brushFrac[k] * trunks[i * 4 + 3];
            return b <= RadiusOf(i) + 0.3f ? 0f : Mathf.Min(b, MaxBrush);
        }

        /// <summary>A plain table: base and RADIUS per tree.</summary>
        public void SetTrunks(List<Vector4> list)
        {
            trunks = new float[list.Count * 4];
            for (int i = 0; i < list.Count; i++)
            {
                trunks[i * 4] = list[i].x; trunks[i * 4 + 1] = list[i].y;
                trunks[i * 4 + 2] = list[i].z; trunks[i * 4 + 3] = list[i].w;
            }
            cells = new byte[0];
            byCell = null; brushByCell.Clear();
        }

        /// <summary>Editor side, a forest: base and CARD WIDTH per tree, the
        /// atlas cell each wears, and what the composer measured off the
        /// billboards (<paramref name="trunkFracByCell"/>: 16;
        /// <paramref name="brushFracByDressCell"/>: dresses x 16).</summary>
        public void SetForest(List<Vector4> list, List<byte> cellOf,
                              float[] trunkFracByCell, float[] brushFracByDressCell)
        {
            SetTrunks(list);
            cells = cellOf.ToArray();
            trunkFrac = (float[])trunkFracByCell.Clone();
            brushFrac = (float[])brushFracByDressCell.Clone();
        }

        // ------------------------------------------------------------------
        //  The grid
        // ------------------------------------------------------------------
        Dictionary<long, List<int>> byCell;
        /// <summary>Per grid cell, the trees that HAVE brush in the dress the
        /// game is wearing — built the first time a car comes near, so the
        /// per-step walk is over a handful of trees rather than all of them.</summary>
        readonly Dictionary<long, List<int>> brushByCell = new Dictionary<long, List<int>>();
        int brushDress = -1;
        /// <summary>The season dress the game is wearing, asked once a second.</summary>
        int wornDress;

        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
        static int CellOf(float v) => Mathf.FloorToInt(v / Cell);

        void EnsureGrid()
        {
            if (byCell != null) return;
            byCell = new Dictionary<long, List<int>>();
            for (int i = 0; i < Count; i++)
            {
                long k = Key(CellOf(trunks[i * 4]), CellOf(trunks[i * 4 + 2]));
                if (!byCell.TryGetValue(k, out var l)) byCell[k] = l = new List<int>();
                l.Add(i);
            }
        }

        /// <summary>Is there a trunk within <paramref name="reach"/> of this
        /// point in plan? The audit's question, answerable in edit mode.</summary>
        public bool Has(Vector3 at, float reach)
        {
            EnsureGrid();
            int cx = CellOf(at.x), cz = CellOf(at.z);
            float r2 = reach * reach;
            for (int x = cx - 1; x <= cx + 1; x++)
                for (int z = cz - 1; z <= cz + 1; z++)
                {
                    if (!byCell.TryGetValue(Key(x, z), out var l)) continue;
                    foreach (int i in l)
                    {
                        float dx = trunks[i * 4] - at.x, dz = trunks[i * 4 + 2] - at.z;
                        if (dx * dx + dz * dz <= r2) return true;
                    }
                }
            return false;
        }

        // ------------------------------------------------------------------
        //  Standing them up round the cars
        // ------------------------------------------------------------------
        readonly Dictionary<long, GameObject> live = new Dictionary<long, GameObject>();
        readonly List<CarController> cars = new List<CarController>();
        readonly Dictionary<CarController, long> lastCell = new Dictionary<CarController, long>();
        readonly HashSet<long> wanted = new HashSet<long>();
        readonly List<long> doomed = new List<long>();
        /// <summary>Cells in a car's 3x3 that are not standing yet, nearest
        /// first. The car's OWN cell is never queued — it is stood at once.</summary>
        readonly List<long> pending = new List<long>();
        float rescanAt;

        /// <summary>How many capsules are standing right now. For the
        /// play-mode check.</summary>
        public int LiveColliders
        {
            get
            {
                int n = 0;
                foreach (var go in live.Values) if (go != null) n += go.GetComponents<CapsuleCollider>().Length;
                return n;
            }
        }

        /// <summary>Steps on which some car was inside some tree's brush. For
        /// the checks: a car that drove through a crown and came out with
        /// this at zero was not slowed by it.</summary>
        public int BrushContacts { get; private set; }

        readonly Dictionary<CarController, int> brushSteps = new Dictionary<CarController, int>();

        /// <summary>Steps THIS car has spent in the brush — the play check
        /// asks about the car it is driving, not about an AI that has gone
        /// into the trees somewhere up the road.</summary>
        public int BrushStepsOf(CarController c) =>
            c != null && brushSteps.TryGetValue(c, out int n) ? n : 0;

        void Awake() => EnsureGrid();

        void FixedUpdate()
        {
            // Awake never runs in edit mode, where the crash harness steps
            // this by hand.
            EnsureGrid();
            bool moved = false;
            // New cars join late — the handoff can swap a field in after
            // Awake — so the list is refreshed once a second. A car already in
            // it is followed every step.
            if (Time.time >= rescanAt)
            {
                rescanAt = Time.time + 1f;
                cars.Clear();
                cars.AddRange(FindObjectsByType<CarController>(FindObjectsSortMode.None));
                moved = true;
                // The calendar does not turn over mid-race, but a preview tool
                // can re-dress the scene under a running table.
                wornDress = Seasons.CurrentDress;
            }
            for (int i = 0; i < cars.Count; i++)
            {
                var c = cars[i];
                if (c == null) { moved = true; continue; }
                Vector3 p = c.transform.position;
                long k = Key(CellOf(p.x), CellOf(p.z));
                if (!lastCell.TryGetValue(c, out long was) || was != k) { lastCell[c] = k; moved = true; }
            }
            if (moved) Refresh();
            StandPending();
            Brush();
        }

        void Refresh()
        {
            // Every cell in the 3x3 round each car is wanted standing...
            wanted.Clear();
            pending.Clear();
            foreach (var c in cars)
            {
                if (c == null) continue;
                Vector3 p = c.transform.position;
                int cx = CellOf(p.x), cz = CellOf(p.z);
                for (int x = cx - 2; x <= cx + 2; x++)
                    for (int z = cz - 2; z <= cz + 2; z++)
                    {
                        long k = Key(x, z);
                        bool near = Mathf.Abs(x - cx) <= 1 && Mathf.Abs(z - cz) <= 1;
                        if (near && !live.ContainsKey(k))
                        {
                            // The cell the car is IN cannot wait a step. Its
                            // neighbours are forty metres of lead away and
                            // can: see StandPending.
                            if (x == cx && z == cz) Stand(k);
                            else if (!pending.Contains(k)) pending.Add(k);
                        }
                        wanted.Add(k);   // the 5x5 is kept, only the 3x3 is built
                    }
            }
            // ...and any that no car is within two cells of comes down. The
            // gap between the two rings is what stops a car sitting on a cell
            // border from building and tearing down the same trunks every step.
            doomed.Clear();
            foreach (var kv in live)
                if (!wanted.Contains(kv.Key)) doomed.Add(kv.Key);
            foreach (var k in doomed)
            {
                Kill(live[k]);
                live.Remove(k);
            }
        }

        /// <summary>
        /// One neighbouring cell a step. A dense stage cell is sixty capsules,
        /// and a car crossing a cell border wants three new cells at once: on
        /// a phone that is a visible hitch every forty metres if it is all
        /// done in the step the border is crossed. The neighbours are a whole
        /// cell ahead of the bumper (27 steps at 90 m/s), so they are stood one
        /// per step instead; at the start of a race the whole block is up
        /// within nine steps, before the lights have begun to count.
        /// </summary>
        void StandPending()
        {
            while (pending.Count > 0)
            {
                long k = pending[pending.Count - 1];
                pending.RemoveAt(pending.Count - 1);
                if (live.ContainsKey(k)) continue;
                Stand(k);
                // An empty cell costs nothing: keep going until one with
                // trees in it has been stood.
                if (live[k] != null) break;
            }
        }

        /// <summary>The harness steps this in EDIT mode, where Destroy
        /// throws.</summary>
        static void Kill(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        void Stand(long k)
        {
            if (!byCell.TryGetValue(k, out var list)) { live[k] = null; return; }
            // One object per cell carrying every trunk in it as its own
            // capsule, at the world origin so each capsule's centre IS the
            // trunk's world position. Not parented: a stage's forest root is
            // not guaranteed to sit at the origin unscaled.
            var go = new GameObject("Trunks");
            go.layer = SolidLayer;
            go.isStatic = true;
            float h = trunkHeight;
            foreach (int i in list)
            {
                var cap = go.AddComponent<CapsuleCollider>();
                float r = RadiusOf(i);
                cap.direction = 1;
                cap.radius = r;
                cap.height = Mathf.Max(h, r * 2f + 0.01f);
                cap.center = new Vector3(trunks[i * 4], trunks[i * 4 + 1] + h * 0.5f, trunks[i * 4 + 2]);
            }
            live[k] = go;
        }

        // ------------------------------------------------------------------
        //  Brush: the foliage a car meets before it meets the trunk
        // ------------------------------------------------------------------

        List<int> BrushIn(long k, int dress)
        {
            if (brushByCell.TryGetValue(k, out var l)) return l;
            l = null;
            if (byCell.TryGetValue(k, out var all))
                foreach (int i in all)
                    if (BrushOf(i, dress) > 0f) (l ??= new List<int>()).Add(i);
            brushByCell[k] = l;
            return l;
        }

        void Brush()
        {
            if (!IsForest || brushFrac.Length < CellsPerAtlas) return;
            int dress = Mathf.Clamp(wornDress, 0, brushFrac.Length / CellsPerAtlas - 1);
            if (dress != brushDress) { brushByCell.Clear(); brushDress = dress; }

            for (int n = 0; n < cars.Count; n++)
            {
                var c = cars[n];
                if (c == null || c.Body == null || c.Body.isKinematic) continue;
                // NEVER ON THE TARMAC. The builder keeps low crowns back from
                // the road (see the forest pass), but a crown is a circle and
                // a road bends: a car with its wheels on the road is not in
                // anybody's leaves, whatever the geometry says.
                if (c.onRoad) continue;
                Vector3 v = c.Body.linearVelocity; v.y = 0f;
                if (v.sqrMagnitude < 0.25f) continue;
                Vector3 p = c.transform.position;
                int cx = CellOf(p.x), cz = CellOf(p.z);
                float deepest = 0f;
                for (int x = cx - 1; x <= cx + 1; x++)
                    for (int z = cz - 1; z <= cz + 1; z++)
                    {
                        var l = BrushIn(Key(x, z), dress);
                        if (l == null) continue;
                        foreach (int i in l)
                        {
                            float dy = p.y - trunks[i * 4 + 1];
                            if (dy < -1.5f || dy > 3.5f) continue;      // the crown is up here, not down the bank
                            float reach = BrushOf(i, dress) + CarHalfWidth;
                            float dx = trunks[i * 4] - p.x, dz = trunks[i * 4 + 2] - p.z;
                            float d2 = dx * dx + dz * dz;
                            if (d2 >= reach * reach) continue;
                            // A metre in, the leaves have all of the car.
                            deepest = Mathf.Max(deepest, Mathf.Clamp01(reach - Mathf.Sqrt(d2)));
                        }
                    }
                if (deepest <= 0f) continue;
                // The DEEPEST crown, not the sum: two trees that overlap are
                // one thicket, not twice the hedge.
                c.Body.AddForce(-v * (BrushDrag * deepest), ForceMode.Acceleration);
                BrushContacts++;
                brushSteps.TryGetValue(c, out int had);
                brushSteps[c] = had + 1;
            }
        }

        void OnDestroy()
        {
            foreach (var go in live.Values) Kill(go);
            live.Clear();
        }
    }
}
