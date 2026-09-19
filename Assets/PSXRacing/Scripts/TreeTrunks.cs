using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The trunks of a forest too big to bake a collider per tree.
    ///
    /// Owner, 2026-09-19: "Trees should also occupy space with their trunks,
    /// stopping a car if it drives into it." A circuit's forty-odd trees each
    /// carry their own capsule. A mountain stage plants ten to sixteen
    /// THOUSAND, merged into chunk meshes, and a baked collider apiece is a
    /// scene of fifteen thousand components the phone has to deserialise and
    /// hold for a race that passes within reach of a fraction of them.
    ///
    /// So the builder writes the trunks as a TABLE — base and radius, four
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
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class TreeTrunks : MonoBehaviour
    {
        /// <summary>x, y, z of the trunk's BASE in world space, then its radius
        /// — four floats per tree, in planting order.</summary>
        [SerializeField] float[] trunks = new float[0];

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

        public int Count => trunks != null ? trunks.Length / 4 : 0;

        public Vector3 BaseOf(int i) => new Vector3(trunks[i * 4], trunks[i * 4 + 1], trunks[i * 4 + 2]);
        public float RadiusOf(int i) => trunks[i * 4 + 3];

        /// <summary>Editor side: the builder hands over the whole table.</summary>
        public void SetTrunks(List<Vector4> list)
        {
            trunks = new float[list.Count * 4];
            for (int i = 0; i < list.Count; i++)
            {
                trunks[i * 4] = list[i].x; trunks[i * 4 + 1] = list[i].y;
                trunks[i * 4 + 2] = list[i].z; trunks[i * 4 + 3] = list[i].w;
            }
            byCell = null;
        }

        // ------------------------------------------------------------------
        //  The grid
        // ------------------------------------------------------------------
        Dictionary<long, List<int>> byCell;

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
        }

        void Refresh()
        {
            // Stand up every cell in the 3x3 round each car...
            wanted.Clear();
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
                        if (near && !live.ContainsKey(k)) Stand(k);
                        wanted.Add(k);   // the 5x5 is kept, only the 3x3 is built
                    }
            }
            // ...and take down any that no car is within two cells of. The
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
                float r = trunks[i * 4 + 3];
                cap.direction = 1;
                cap.radius = r;
                cap.height = Mathf.Max(h, r * 2f + 0.01f);
                cap.center = new Vector3(trunks[i * 4], trunks[i * 4 + 1] + h * 0.5f, trunks[i * 4 + 2]);
            }
            live[k] = go;
        }

        void OnDestroy()
        {
            foreach (var go in live.Values) Kill(go);
            live.Clear();
        }
    }
}
