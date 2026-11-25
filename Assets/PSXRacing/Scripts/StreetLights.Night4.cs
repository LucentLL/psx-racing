using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// NIGHT 4 (night round 4, 2026-11-23). The owner on NIGHT 3 (PC61, a replay on
    /// a mountain stage): "why does light go through walls, highway medians, etc?";
    /// on the clips: "Why is the sky so black with navy right next to it in one
    /// video. then the next video the sky is foggy and bright on the horizon?"
    ///
    /// A LAMP LIGHTS ONLY WHAT IT CAN SEE. The static lamp field
    /// (StreetLights.Night3.cs) is a top-down splat: nothing blocked it, so a
    /// lamp's pool and glow lay on the far side of a building, a wall, a deck or
    /// a cut. Now each field lamp's view of the ground round it is found ONCE, when
    /// the lamp first joins the field (the tile that holds it streams in, at the
    /// ring's edge): from its head, rays to the ground on 48 bearings x 13 radii
    /// (0 to 40 m), batched on the job threads (RaycastCommand). On each bearing
    /// the ground the head cannot see is kept as up to two shadowed stretches
    /// (where a solid cuts the line, to where the ground beyond it comes back into
    /// view - behind a waist-high barrier that is a metre or two, behind a building
    /// or a wall over the head it is all the rest). They are stored in a small
    /// atlas, a row of 50 texels per lamp (the 48 bearings and a wrap texel either
    /// end), and the splat (Resources/NightSplat/PSXLampSplat pass 0) multiplies
    /// each lamp's pool and glow by what its head sees: one fetch per splat texel,
    /// paid only when the field is splatted (a lamp change). Nothing at run time:
    /// the surfaces still read the field with one fetch. Thin things (posts,
    /// trunks, signal poles: under 0.9 m across) and cars do not block.
    /// PSX_NIGHT4=0: NIGHT 3's field, unblocked.
    ///
    /// ONE NIGHT SKY over the lit city (PSXSky.shader, PSXAtmosphere.cginc): see
    /// <see cref="PushNight4Globals"/>.
    /// </summary>
    public static partial class StreetLights
    {
        /// <summary>The round's one switch (live: the tools flip it in-run).</summary>
        public static bool Night4 = System.Environment.GetEnvironmentVariable("PSX_NIGHT4") != "0";

        // ---- the static field's occlusion ----
        public const int N4Bearings = 48;
        const int N4Block = N4Bearings + 2;        // + a wrap texel either end
        const int N4PerRow = 80;                   // lamps per atlas row (4000 texels)
        static readonly float[] N4Radii = { 1.5f, 3f, 4.5f, 6f, 8f, 10f, 12.5f, 15f, 18f, 22f, 27f, 33f, 40f };
        /// <summary>"Nothing in the way" (m): past the glow's reach, near it, so a bearing's
        /// shadow edge interpolates to its neighbour's over half a bearing.</summary>
        public const float N4Clear = 45f;
        /// <summary>A thing narrower than this (m) across in plan casts no shadow in the field.</summary>
        public const float N4ThinM = 0.9f;
        static Texture2D n4Atlas;
        static ushort[] n4Data;
        static int n4Rows;
        static bool n4Dirty;
        static Vector4[] n4Vis = new Vector4[0];   // per registry slot x bearing, flattened
        static bool[] n4Known = new bool[0];
        static Vector3[] n4At = new Vector3[0];
        static readonly List<int> n4Todo = new List<int>();
        /// <summary>Lamps a look finds while driving (the rest wait for the next look, half a second on).</summary>
        public static int N4LampsPerLook = 24;
        static bool n4Backlog, n4EyeKnown;
        static Vector3 n4LastEye;
        /// <summary>Field lamps waiting for the tiles round them (not splatted yet).</summary>
        static int n4Waiting;
        static readonly Dictionary<EntityId, bool> n4Thin = new Dictionary<EntityId, bool>();
        /// <summary>Rays cast for the field's occlusion since load, and the last batch's ms (tools).</summary>
        public static int N4Rays { get; private set; }
        public static float N4LastMs { get; private set; }
        public static int N4Shadowed { get; private set; }
        static readonly int LampVisId = Shader.PropertyToID("_LampVis");

        /// <summary>Layers that cast the field's shadows: everything but the cars (2) and the
        /// foliage (10: city tree crowns, median plants), whose own colliders are none or thin.</summary>
        public static int N4Mask => ~((1 << 2) | (1 << 10));

        /// <summary>Is this collider thin in plan (a post, a trunk, a signal pole): no shadow.</summary>
        /// <summary>(4b) By the hit's id first: hit.collider is a managed lookup, a thousand of them a lamp.</summary>
        internal static bool N4IsThinHit(in RaycastHit hit)
        {
            var id = hit.colliderEntityId;
            if (n4Thin.TryGetValue(id, out bool t)) return t;
            return N4IsThin(hit.collider, id);
        }

        internal static bool N4IsThin(Collider c, EntityId id)
        {
            if (c == null) return true;
            if (n4Thin.TryGetValue(id, out bool t)) return t;
            var b = c.bounds;
            t = Mathf.Max(b.size.x, b.size.z) < N4ThinM || c.isTrigger;
            if (n4Thin.Count > 200000) n4Thin.Clear();
            n4Thin[id] = t;
            return t;
        }

        /// <summary>The atlas slot of a lamp: u of its first bearing's texel (texels, block start + 1)
        /// and v (0..1, its row's centre); -1 when NIGHT 4 is off or the lamp has no view yet.</summary>
        static Vector2 N4Slot(int slot)
        {
            if (!Night4 || n4Atlas == null || slot >= n4Known.Length || !n4Known[slot]) return new Vector2(-1f, 0f);
            int col = slot % N4PerRow, row = slot / N4PerRow;
            if (row >= n4Rows) return new Vector2(-1f, 0f);
            float W = N4PerRow * N4Block;
            return new Vector2((col * N4Block + 1f) / W, (row + 0.5f) / n4Rows);
        }

        /// <summary>The splat's atlas constants (PSXLampSplat pass 0: _SplatR.z = bearings / atlas width).</summary>
        static float N4UScale => (float)N4Bearings / (N4PerRow * N4Block);

        /// <summary>Find the view of every lit field lamp that has none (N3BuildMesh, before the mesh).</summary>
        static void N4UpdateVis()
        {
            if (!Night4 || !Application.isPlaying) return;
            if (n4Known.Length < entries.Length)
            {
                System.Array.Resize(ref n4Known, entries.Length);
                System.Array.Resize(ref n4At, entries.Length);
                System.Array.Resize(ref n4Vis, entries.Length * N4Bearings);
            }
            n4Todo.Clear();
            n4Waiting = 0;
            var world = City.CityWorld.Active;
            for (int i = 0; i < high; i++)
            {
                ref Entry e = ref entries[i];
                if (!e.used || !e.field || e.kind != Kind.Street) continue;
                if (n4Known[i] && (n4At[i] - e.pos).sqrMagnitude < 0.01f) continue;
                // only once every tile its light reaches is standing (a building on the next tile
                // over would otherwise be missed for good); until then it is not splatted at all -
                // which happens only at the ring's edge, past the far plane
                // (4b) a lamp found waiting is asked again only once the ring has changed
                if (world != null)
                {
                    if (n4WaitSeen.Length < entries.Length) System.Array.Resize(ref n4WaitSeen, entries.Length);
                    int liveNow = world.LiveTiles + 1;
                    if (n4WaitSeen[i] == liveNow) { n4Waiting++; continue; }
                    if (!N4NeighboursLive(world, e.pos)) { n4WaitSeen[i] = liveNow; n4Waiting++; continue; }
                    n4WaitSeen[i] = 0;
                }
                n4Todo.Add(i);
            }
            // A BUDGET while driving: a tile row streaming in at the ring's edge (past the far plane) is
            // found 24 lamps a look (~7 ms on the job threads, measured 0.27 ms a lamp in a 1000-lamp load);
            // at a load or a jump (a replay cut, a teleport, the first look) the whole ring at once
            n4Backlog = false;
            var camN = Camera.main;
            Vector3 eyeN = camN != null ? camN.transform.position : Vector3.zero;
            bool jumped = !n4EyeKnown || (eyeN - n4LastEye).sqrMagnitude > 200f * 200f;
            n4LastEye = eyeN; n4EyeKnown = true;
            if (!jumped && n4Todo.Count > N4LampsPerLook)
            {
                n4Todo.Sort((a, b) => (entries[a].pos - eyeN).sqrMagnitude.CompareTo((entries[b].pos - eyeN).sqrMagnitude));
                n4Todo.RemoveRange(N4LampsPerLook, n4Todo.Count - N4LampsPerLook);
                n4Backlog = true;
            }
            if (n4Todo.Count == 0) { N4Upload(); return; }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // 4b: WHILE DRIVING the batch goes to the job threads and is read back on later frames, a few
            // lamps a frame (N4Tick); no SyncTransforms (the tiles' colliders stand from the physics step
            // after they do). At a load or a jump (inside the loading screen, a replay cut) it waits as before.
            if (!jumped && N4Async)
            {
                if (!n4JobPending) N4ScheduleAsync(n4Todo);
                N4LastMs = (float)clock.Elapsed.TotalMilliseconds;
                Night4Clock.View(N4LastMs);
                return;
            }
            N4DropAsync();
            Physics.SyncTransforms();
            // in chunks (a whole ring at load is thousands of lamps: one batch would hold hundreds of MB)
            int shadowed = 0, total = 0;
            for (int c0 = 0; c0 < n4Todo.Count; c0 += 192)
            {
                n4Chunk.Clear();
                for (int c = c0; c < Mathf.Min(n4Todo.Count, c0 + 192); c++) n4Chunk.Add(n4Todo[c]);
                total += N4CastChunk(n4Chunk, ref shadowed);
            }
            N4Rays += total;
            N4Shadowed = shadowed;
            // the atlas rows of every slot just found
            N4EnsureAtlas();
            foreach (int slot in n4Todo) N4WriteRow(slot);
            N4Upload();
            N4LastMs = (float)clock.Elapsed.TotalMilliseconds;
            Night4Clock.View(N4LastMs);
            if (n4Todo.Count >= 50) Debug.Log("[StreetLights] NIGHT 4: the view of " + n4Todo.Count + " lamps (" + total + " rays, " + shadowed + " bearings shadowed) in " + N4LastMs.ToString("0") + " ms; " + n4Waiting + " wait for their tiles");
        }

        /// <summary>Lamps wait for the tiles round them: worth a look again only once the ring changed.</summary>
        static int n4LiveSeen = -1;
        static bool N4WaitingDue()
        {
            if (n4Backlog) return true;
            if (n4Waiting <= 0) return false;
            var w = City.CityWorld.Active;
            int live = w != null ? w.LiveTiles : -1;
            if (live == n4LiveSeen) return false;
            n4LiveSeen = live;
            return true;
        }

        static readonly List<int> n4Chunk = new List<int>();
        /// <summary>One batch of lamps' views (N4UpdateVis): the rays, then each bearing's shadowed stretches.</summary>
        static int N4CastChunk(List<int> todo, ref int shadowed)
        {
            int K = N4Radii.Length, per = N4Bearings * K;
            int total = todo.Count * per;
            var cmds = new NativeArray<RaycastCommand>(total, Allocator.TempJob);
            var hits = new NativeArray<RaycastHit>(total * 2, Allocator.TempJob);
            var qp = new QueryParameters(N4Mask, false, QueryTriggerInteraction.Ignore, false);
            var heads = new Vector3[todo.Count];
            var grounds = new float[todo.Count];
            N4Commands(todo, cmds, heads, grounds);
            var job = RaycastCommand.ScheduleBatch(cmds, hits, 64, 2);
            job.Complete();
            for (int t = 0; t < todo.Count; t++) N4ReadLamp(hits, t, todo[t], heads[t], grounds[t], ref shadowed);
            cmds.Dispose(); hits.Dispose();
            return total;
        }

        /// <summary>The rays of a batch of lamps: head to ground on every bearing and radius.</summary>
        static void N4Commands(List<int> todo, NativeArray<RaycastCommand> cmds, Vector3[] heads, float[] grounds)
        {
            int K = N4Radii.Length, per = N4Bearings * K;
            var qp = new QueryParameters(N4Mask, false, QueryTriggerInteraction.Ignore, false);
            for (int t = 0; t < todo.Count; t++)
            {
                ref Entry e = ref entries[todo[t]];
                if (!e.groundKnown)
                {
                    e.groundKnown = true;
                    e.ground = Physics.Raycast(e.pos + Vector3.down * 0.3f, Vector3.down, out var gh, 60f, ~(1 << 2), QueryTriggerInteraction.Ignore) ? gh.point.y : e.pos.y - 9f;
                }
                // the head a hair under its lens, so the luminaire's own box (if any) is behind the ray
                var h = e.pos + Vector3.down * 0.15f;
                heads[t] = h; grounds[t] = e.ground;
                for (int b = 0; b < N4Bearings; b++)
                {
                    float a = (b + 0.5f) / N4Bearings * Mathf.PI * 2f;
                    float cx = Mathf.Cos(a), cz = Mathf.Sin(a);
                    for (int k = 0; k < K; k++)
                    {
                        var target = new Vector3(h.x + cx * N4Radii[k], e.ground, h.z + cz * N4Radii[k]);
                        var d = target - h;
                        float len = d.magnitude;
                        cmds[t * per + b * K + k] = new RaycastCommand(h, d / Mathf.Max(len, 1e-4f), qp, len + 1.0f);
                    }
                }
            }
        }

        /// <summary>One lamp's view from its rays' hits (lamp t of a batch): each bearing's shadowed stretches.</summary>
        static void N4ReadLamp(NativeArray<RaycastHit> hits, int t, int slot, Vector3 h, float gy, ref int shadowed)
        {
            int K = N4Radii.Length, per = N4Bearings * K;
            for (int b = 0; b < N4Bearings; b++)
            {
                float a = (b + 0.5f) / N4Bearings * Mathf.PI * 2f;
                // per radius: lit, or the plan distance of what cut the line
                float s1 = N4Clear, e1 = 99f, s2 = N4Clear, e2 = 99f;
                int runs = 0; bool inShadow = false; float lastBlockedR = 0f;
                for (int k = 0; k < K; k++)
                {
                    int ci = t * per + b * K + k;
                    var tgt = new Vector3(h.x + Mathf.Cos(a) * N4Radii[k], gy, h.z + Mathf.Sin(a) * N4Radii[k]);
                    float len = Vector3.Distance(h, tgt);
                    bool blocked = false; float hitR = 0f;
                    // the nearest hit that is neither thin nor the ground itself
                    float best = float.MaxValue; RaycastHit bh = default;
                    for (int m = 0; m < 2; m++)
                    {
                        var hit = hits[ci * 2 + m];
                        if (hit.normal == Vector3.zero) continue;   // (no hit)
                        if (hit.distance >= best) continue;
                        if (N4IsThinHit(hit)) continue;
                        best = hit.distance; bh = hit;
                    }
                    if (best < float.MaxValue && best < len - 0.6f)
                    {
                        // ground rising toward the target is ground, not a wall
                        bool groundRise = bh.normal.y > 0.65f && Mathf.Abs(bh.point.y - gy) < 2.5f;
                        if (!groundRise)
                        {
                            blocked = true;
                            hitR = new Vector2(bh.point.x - h.x, bh.point.z - h.z).magnitude;
                        }
                    }
                    if (blocked && !inShadow)
                    {
                        // the shadow starts just past what cut the line (its face stays lit)
                        float lo = k > 0 ? N4Radii[k - 1] : 0f;
                        float st = Mathf.Clamp(hitR + 0.3f, lo, N4Radii[k]);
                        if (runs == 0) { s1 = st; e1 = 99f; } else if (runs == 1) { s2 = st; e2 = 99f; } else e2 = 99f;
                        // a third run joins the second (conservative: dark to its end)
                        inShadow = true;
                        runs++;
                    }
                    if (blocked) lastBlockedR = N4Radii[k];
                    if (!blocked && inShadow)
                    {
                        float back = 0.5f * (lastBlockedR + N4Radii[k]);
                        if (runs == 1) e1 = back;
                        else if (runs == 2) e2 = back;
                        else e2 = back;   // (a third run: the second stretches over it)
                        inShadow = false;
                    }
                }
                if (runs > 0) shadowed++;
                n4Vis[slot * N4Bearings + b] = new Vector4(s1, e1, s2, e2);
            }
            n4Known[slot] = true;
            n4At[slot] = entries[slot].pos;
        }


        // ---- 4b: the street-lamp view while driving, off the main thread ----
        /// <summary>false = round 4's way (24 lamps found in one frame, waited for: the "before").</summary>
        public static bool N4Async = System.Environment.GetEnvironmentVariable("PSX_N4_ASYNC") != "0";
        /// <summary>Main-thread milliseconds a frame may spend reading lamps' rays back.</summary>
        public static float N4ReadBudgetMs = 0.25f;
        static bool n4JobPending;
        static int[] n4WaitSeen = new int[0];
        static NativeArray<Vector3> n4JH;
        static NativeArray<float> n4JG, n4RadiiN;
        static NativeArray<Vector2> n4Dirs;
        static Unity.Jobs.JobHandle n4Job;
        static NativeArray<RaycastCommand> n4Cmds;
        static NativeArray<RaycastHit> n4Hits;
        static readonly List<int> n4JobSlots = new List<int>();
        static Vector3[] n4JobHeads = new Vector3[0];
        static float[] n4JobGrounds = new float[0];
        static int n4JobRead;
        static readonly HashSet<int> n4RowsTouched = new HashSet<int>();
        static Texture2D n4Staging;
        static bool n4QuitHooked;

        static void N4ScheduleAsync(List<int> todo)
        {
            int per = N4Bearings * N4Radii.Length, total = todo.Count * per;
            if (!n4QuitHooked) { n4QuitHooked = true; Application.quitting += N4DropAsync; }
            if (!n4Cmds.IsCreated || n4Cmds.Length < total)
            {
                if (n4Cmds.IsCreated) n4Cmds.Dispose();
                if (n4Hits.IsCreated) n4Hits.Dispose();
                int cap = Mathf.Max(total, per * N4LampsPerLook);
                n4Cmds = new NativeArray<RaycastCommand>(cap, Allocator.Persistent);
                n4Hits = new NativeArray<RaycastHit>(cap * 2, Allocator.Persistent);
            }
            n4JobSlots.Clear(); n4JobSlots.AddRange(todo);
            if (n4JobHeads.Length < todo.Count) { n4JobHeads = new Vector3[todo.Count]; n4JobGrounds = new float[todo.Count]; }
            if (!n4JH.IsCreated || n4JH.Length < todo.Count)
            {
                if (n4JH.IsCreated) { n4JH.Dispose(); n4JG.Dispose(); }
                int capL = Mathf.Max(todo.Count, N4LampsPerLook);
                n4JH = new NativeArray<Vector3>(capL, Allocator.Persistent);
                n4JG = new NativeArray<float>(capL, Allocator.Persistent);
            }
            if (!n4Dirs.IsCreated)
            {
                n4Dirs = new NativeArray<Vector2>(N4Bearings, Allocator.Persistent);
                for (int b = 0; b < N4Bearings; b++) { float a = (b + 0.5f) / N4Bearings * Mathf.PI * 2f; n4Dirs[b] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); }
                n4RadiiN = new NativeArray<float>(N4Radii, Allocator.Persistent);
            }
            for (int t = 0; t < todo.Count; t++)
            {
                ref Entry e = ref entries[todo[t]];
                if (!e.groundKnown)
                {
                    e.groundKnown = true;
                    e.ground = Physics.Raycast(e.pos + Vector3.down * 0.3f, Vector3.down, out var gh, 60f, ~(1 << 2), QueryTriggerInteraction.Ignore) ? gh.point.y : e.pos.y - 9f;
                }
                var h = e.pos + Vector3.down * 0.15f;
                n4JobHeads[t] = h; n4JobGrounds[t] = e.ground;
                n4JH[t] = h; n4JG[t] = e.ground;
            }
            // the commands on the job threads as well: the main thread only schedules
            var fill = new N4FieldFillJob
            {
                heads = n4JH, grounds = n4JG, dirs = n4Dirs, radii = n4RadiiN,
                qp = new QueryParameters(N4Mask, false, QueryTriggerInteraction.Ignore, false), cmds = n4Cmds.GetSubArray(0, total),
            }.Schedule(total, 128);
            n4Job = RaycastCommand.ScheduleBatch(n4Cmds.GetSubArray(0, total), n4Hits.GetSubArray(0, total * 2), 64, 2, fill);
            Night4PhysicsFence.Ensure();
            Unity.Jobs.JobHandle.ScheduleBatchedJobs();
            n4JobPending = true;
            n4JobRead = 0;
        }

        /// <summary>Before a physics step (Night4PhysicsFence): the lamps' job finished (read back later).</summary>
        internal static void N4Fence() { if (n4JobPending) n4Job.Complete(); }

        /// <summary>A jump or a load while a batch is out: wait for it (its lamps are found again with the rest).</summary>
        static void N4DropAsync()
        {
            if (n4JobPending) { n4Job.Complete(); n4JobPending = false; n4JobSlots.Clear(); }
        }

        /// <summary>Every frame (PushN3): once the job threads are done, read a few lamps back within the
        /// budget; when the batch is all read, its atlas rows go up (a row each, not the atlas) and the
        /// field is splatted again with them.</summary>
        static void N4Tick()
        {
            if (!n4JobPending || !n4Job.IsCompleted) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            n4Job.Complete();   // (done: costs nothing)
            int shadowed = 0;
            N4EnsureAtlasRows();
            while (n4JobRead < n4JobSlots.Count)
            {
                int t = n4JobRead++;
                int slot = n4JobSlots[t];
                if (slot < high && entries[slot].used) N4ReadLamp(n4Hits, t, slot, n4JobHeads[t], n4JobGrounds[t], ref shadowed);
                if (clock.Elapsed.TotalMilliseconds > N4ReadBudgetMs) break;
            }
            if (n4JobRead >= n4JobSlots.Count)
            {
                n4JobPending = false;
                N4EnsureAtlas();
                n4RowsTouched.Clear();
                foreach (int slot in n4JobSlots) { if (slot < high) { N4WriteRowData(slot); n4RowsTouched.Add(slot / N4PerRow); } }
                N4UploadRows(n4RowsTouched);
                N4Rays += n4JobSlots.Count * N4Bearings * N4Radii.Length;
                n4JobSlots.Clear();
                N4Touch();   // the lamps join the splat at its next gathering
            }
            Night4Clock.Tick((float)clock.Elapsed.TotalMilliseconds);
        }

        static void N4EnsureAtlasRows() { }

        /// <summary>One lamp's row into the CPU copy only (the upload is per row, N4UploadRows).</summary>
        static void N4WriteRowData(int slot)
        {
            bool dirty = n4Dirty;
            N4WriteRow(slot);
            n4Dirty = dirty;
        }

        /// <summary>The touched rows to the GPU, one 4000-texel row each (a whole-atlas Apply was ~4 MB).</summary>
        static void N4UploadRows(HashSet<int> rows)
        {
            if (n4Atlas == null) return;
            if (n4Dirty) { N4Upload(); return; }   // (a new or grown atlas goes up whole)
            int W = N4PerRow * N4Block;
            if (n4Staging == null || n4Staging.width != W)
            {
                if (n4Staging != null) Object.Destroy(n4Staging);
                n4Staging = new Texture2D(W, 1, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.DontSave, filterMode = FilterMode.Point };
            }
            var row = new ushort[W * 4];
            foreach (int r in rows)
            {
                if (r >= n4Rows) continue;
                System.Array.Copy(n4Data, r * W * 4, row, 0, W * 4);
                n4Staging.SetPixelData(row, 0);
                n4Staging.Apply(false, false);
                Graphics.CopyTexture(n4Staging, 0, 0, 0, 0, W, 1, n4Atlas, 0, 0, 0, r);
            }
        }

        static bool N4NeighboursLive(City.CityWorld world, Vector3 p)
        {
            const float r = 42f;
            return world.TileLive(new Vector2(p.x - r, p.z - r)) && world.TileLive(new Vector2(p.x + r, p.z - r))
                && world.TileLive(new Vector2(p.x - r, p.z + r)) && world.TileLive(new Vector2(p.x + r, p.z + r));
        }

        /// <summary>Under NIGHT 4 in play, a field lamp joins the splat only once its view is known.</summary>
        static bool N4Withheld(int slot) => Night4 && Application.isPlaying && City.CityWorld.Active != null
                                            && (slot >= n4Known.Length || !n4Known[slot]);

        static void N4EnsureAtlas()
        {
            int rowsNeed = Mathf.Max(8, Mathf.NextPowerOfTwo((high + N4PerRow - 1) / N4PerRow));
            if (n4Atlas != null && n4Rows >= rowsNeed) return;
            int W = N4PerRow * N4Block;
            var old = n4Data; int oldRows = n4Rows;
            n4Rows = rowsNeed;
            n4Data = new ushort[W * n4Rows * 4];
            if (old != null) System.Array.Copy(old, n4Data, Mathf.Min(old.Length, n4Data.Length));
            else
            {
                ushort clear = Mathf.FloatToHalf(N4Clear), far = Mathf.FloatToHalf(99f);
                for (int i = 0; i < n4Data.Length; i += 4) { n4Data[i] = clear; n4Data[i + 1] = far; n4Data[i + 2] = clear; n4Data[i + 3] = far; }
            }
            if (old != null && oldRows < n4Rows)
            {
                ushort clear = Mathf.FloatToHalf(N4Clear), far = Mathf.FloatToHalf(99f);
                for (int i = old.Length; i < n4Data.Length; i += 4) { n4Data[i] = clear; n4Data[i + 1] = far; n4Data[i + 2] = clear; n4Data[i + 3] = far; }
            }
            if (n4Atlas != null) Object.Destroy(n4Atlas);
            n4Atlas = new Texture2D(W, n4Rows, TextureFormat.RGBAHalf, false, true)
            {
                name = "PSXLampVis", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave,
            };
            n4Dirty = true;
        }

        static void N4WriteRow(int slot)
        {
            int col = slot % N4PerRow, row = slot / N4PerRow;
            if (row >= n4Rows) return;
            int W = N4PerRow * N4Block;
            int baseT = (row * W + col * N4Block) * 4;
            for (int x = 0; x < N4Block; x++)
            {
                int b = x == 0 ? N4Bearings - 1 : x == N4Block - 1 ? 0 : x - 1;
                var v = n4Vis[slot * N4Bearings + b];
                int o = baseT + x * 4;
                n4Data[o] = Mathf.FloatToHalf(v.x); n4Data[o + 1] = Mathf.FloatToHalf(v.y);
                n4Data[o + 2] = Mathf.FloatToHalf(v.z); n4Data[o + 3] = Mathf.FloatToHalf(v.w);
            }
            n4Dirty = true;
        }

        static void N4Upload()
        {
            if (!n4Dirty || n4Atlas == null) return;
            n4Atlas.SetPixelData(n4Data, 0);
            n4Atlas.Apply(false, false);
            n4Dirty = false;
        }

        /// <summary>Bind the atlas for the splat (N3Splat).</summary>
        static void N4BindSplat(Material m)
        {
            if (m == null) return;
            if (n4Atlas != null) m.SetTexture(LampVisId, n4Atlas);
        }

        /// <summary>Tools: the field lamp nearest p (plan) and its slot, or -1.</summary>
        public static int N4NearestSlot(Vector3 p, float maxM)
        {
            int best = -1; float bd = maxM * maxM;
            for (int i = 0; i < high; i++)
            {
                ref Entry e = ref entries[i];
                if (!e.used || !e.field || e.kind != Kind.Street) continue;
                float dx = e.pos.x - p.x, dz = e.pos.z - p.z, d2 = dx * dx + dz * dz;
                if (d2 < bd) { bd = d2; best = i; }
            }
            return best;
        }

        /// <summary>Tools: a slot's head, and its view on the bearing toward p (s1 e1 s2 e2), or false.</summary>
        public static bool N4ViewToward(int slot, Vector3 p, out Vector3 head, out Vector4 view)
        {
            head = default; view = new Vector4(N4Clear, 99f, N4Clear, 99f);
            if (slot < 0 || slot >= high || !entries[slot].used) return false;
            head = entries[slot].pos;
            if (slot >= n4Known.Length || !n4Known[slot]) return false;
            float a = Mathf.Atan2(p.z - head.z, p.x - head.x) / (Mathf.PI * 2f);
            if (a < 0f) a += 1f;
            int b = Mathf.Clamp(Mathf.FloorToInt(a * N4Bearings), 0, N4Bearings - 1);
            view = n4Vis[slot * N4Bearings + b];
            return true;
        }

        /// <summary>Tools: splat ONLY the field lamps within <paramref name="m"/> of <paramref name="p"/>
        /// (the leak probe's one lamp); NaN = every lamp.</summary>
        public static Vector3 N4SoloAt = new Vector3(float.NaN, 0f, 0f);
        public static float N4SoloM = 1f;
        static bool N4SoloSkips(Vector3 pos)
        {
            if (float.IsNaN(N4SoloAt.x)) return false;
            float dx = pos.x - N4SoloAt.x, dz = pos.z - N4SoloAt.z;
            return dx * dx + dz * dz > N4SoloM * N4SoloM;
        }
        /// <summary>Tools: the heads of the field street lamps within r (plan) of p.</summary>
        public static void FieldLampsNear(Vector3 p, float r, List<Vector3> heads)
        {
            heads.Clear();
            for (int i = 0; i < high; i++)
            {
                ref Entry e = ref entries[i];
                if (!e.used || !e.field || e.kind != Kind.Street || !e.on) continue;
                float dx = e.pos.x - p.x, dz = e.pos.z - p.z;
                if (dx * dx + dz * dz < r * r) heads.Add(e.pos);
            }
        }

        /// <summary>Tools: the static field's light on an upward surface at p (linear, the hue's
        /// units: the pool and glow with the vertical reach applied, as PSXLamps.cginc's w=3
        /// branch on the ground), read back from the GPU; -1 when there is no field.</summary>
        public static float N3LightAt(Vector3 p)
        {
            if (n3Tex == null || !n3Tex.IsCreated() || float.IsNaN(n3Origin.x)) return -1f;
            int x = Mathf.FloorToInt((p.x - n3Origin.x) / N3TexelM), z = Mathf.FloorToInt((p.z - n3Origin.y) / N3TexelM);
            if (x < 0 || z < 0 || x >= n3Res || z >= n3Res) return -1f;
            var req = UnityEngine.Rendering.AsyncGPUReadback.Request(n3Tex, 0, x, 1, z, 1, 0, 1, TextureFormat.RGBAFloat, null);
            req.WaitForCompletion();
            if (req.hasError) return -1f;
            var c = req.GetData<Color>()[0];
            float hY = n3BaseY + c.b / Mathf.Max(c.g, 1e-4f);
            float un = hY - p.y;
            float vP = Mathf.Clamp01((un + 1f) * 0.5f) * Mathf.Clamp01((16f - un) * 0.25f);
            float vG = Mathf.Clamp01((un + 8f) / 8f) * Mathf.Clamp01((16f - un) * 0.25f);
            return Mathf.Max(c.r, 0f) * vP + Mathf.Max(c.g, 0f) * vG;
        }

        /// <summary>Tools: force the field to be gathered and splatted again now.</summary>
        public static void N4Touch() => n3Version++;

        // ---- ONE NIGHT SKY ----
        // The owner's two clips: on I-77 black cloud shapes with hard edges against a navy sky
        // with stars; on West Trade a pale band along the horizon. Causes, measured off the
        // NIGHT 3 code: (1) a night cloud was lit by next to nothing - the moon's term and a
        // floor - while the clear sky behind it kept its scattered light, so every cloud was a
        // hole darker than the sky round it, its edge as hard as the puff's; (2) the lit city's
        // darkening of the sky (x0.25) was faded in over the first five degrees (saturate(dir.y
        // x 12)), so the low sky kept its old brightness: the pale band; (3) it was gated on the
        // lamp field being on (FieldActive), so it could switch; (4) the PHOTO sky (the game's
        // default) took no darkening at all. NIGHT 4: over the lit city at night the sky is ONE
        // gradient (near-black overhead, a little lighter low down), the source sky's clouds kept
        // only as a faint modulation of it (both skies), the stars over it, and the city's own low
        // warm glow (PSXSky.shader's CityGlow) the only bright thing in it; its weight eases over
        // two seconds and follows only the city being loaded and the hour.
        /// <summary>How much of the source sky's structure is replaced by the gradient (0..1).</summary>
        public static float N4SkyMix = 0.85f;
        /// <summary>The gradient (linear, the sky shader's own range): overhead and at the horizon.</summary>
        public static Color N4SkyZenith = new Color(0.0010f, 0.0012f, 0.0019f);
        public static Color N4SkyHorizon = new Color(0.0026f, 0.0026f, 0.0030f);
        /// <summary>Both gradient stops x this (the tools' variants: a float is what they can set).</summary>
        public static float N4SkyLevel = 1f;
        /// <summary>The city glow's share under NIGHT 4 (x CityFar's own).</summary>
        public static float N4GlowMul = 1f;
        /// <summary>The most a lamp-lit surface's effective albedo reads under NIGHT 4 (PSXLit: a pale
        /// pavement, plaza or face no brighter than lit asphalt), and the share of a face lit above
        /// its lamp's head (PSXLamps.cginc w=3).</summary>
        public static float N4AlbedoCap = 0.12f;
        static float n4CityW;
        static int n4SkyFrame = -1;
        static readonly int N4Id = Shader.PropertyToID("_PSXNight4");
        static readonly int N4ZenId = Shader.PropertyToID("_PSXN4SkyZen");
        static readonly int N4HorId = Shader.PropertyToID("_PSXN4SkyHor");

        /// <summary>_PSXNight4 (x the lit city's night sky weight, y the mix to the gradient, z the
        /// albedo cap, w on) and the gradient; once a frame the city weight eases to its target.</summary>
        static void PushNight4Globals(float night)
        {
            if (!Night4) { Shader.SetGlobalVector(N4Id, Vector4.zero); return; }
            if (Time.frameCount != n4SkyFrame)
            {
                n4SkyFrame = Time.frameCount;
                float target = Night3 && City.CityWorld.Active != null ? 1f : 0f;
                float dt = Application.isPlaying ? Mathf.Min(Time.unscaledDeltaTime, 0.1f) : 1f;
                n4CityW = Mathf.MoveTowards(n4CityW, target, dt * 0.5f);
            }
            float w = n4CityW * Mathf.Clamp01(night);
            Shader.SetGlobalVector(N4Id, new Vector4(w, N4SkyMix, N4AlbedoCap, 1f));
            Shader.SetGlobalVector(N4ZenId, (Vector4)(N4SkyZenith * N4SkyLevel));
            Shader.SetGlobalVector(N4HorId, new Vector4(N4SkyHorizon.r * N4SkyLevel, N4SkyHorizon.g * N4SkyLevel, N4SkyHorizon.b * N4SkyLevel, N4GlowMul));
        }

        /// <summary>NIGHT 4's sky darkening weight for the dynamic sky (x N3SkyMul at a whole city night).</summary>
        static float N4SkyMulNow(float night) => Mathf.Lerp(1f, N3SkyMul, n4CityW * Mathf.Clamp01(night));
    }
}
