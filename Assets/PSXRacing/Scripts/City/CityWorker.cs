using System.Collections.Generic;
using System.Threading;

namespace PSXRacing.City
{
    /// <summary>
    /// THE CITY'S WORKER THREADS (city perf round 1, 2026-10-30; owner: "The
    /// goal is a forza/nfs quality open world in a full windows (and android)
    /// release. But I require web playtesting while away from PC").
    ///
    /// Heavy city work that is PURE - it reads only data that no longer changes
    /// once the city has loaded (the map, the canopy grid, the DEM through a
    /// <see cref="CityElevation.DemReader"/> of its own) and writes only what
    /// it was handed - runs here on a native player: <see cref="Run"/> queues
    /// the work for a background thread and its <c>apply</c> (whatever must
    /// touch the world or a Unity object) comes back to the main thread
    /// through <see cref="Pump"/> (CityWorld.Update, once a frame). A WebGL
    /// player has no threads: there <see cref="Threaded"/> is false and the
    /// caller runs the SAME work on the main thread in its own slices (Run
    /// then just runs it, and applies it, at once). Nothing here may be called
    /// from a worker but the work itself.
    ///
    /// PSX_CITY_THREADS=0 (a tool's environment) or <see cref="Enabled"/> = false
    /// (?ab=CityWorker.Enabled:0 on a perf tour): the main-thread path everywhere.
    /// </summary>
    public static class CityWorker
    {
        public static bool Enabled = System.Environment.GetEnvironmentVariable("PSX_CITY_THREADS") != "0";

        /// <summary>Background threads do the work (a native player with
        /// cores to spare), or the caller slices it on the main thread.</summary>
        public static bool Threaded
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return false;
#else
                return Enabled && UnityEngine.SystemInfo.processorCount >= 4;
#endif
            }
        }

        public sealed class Job
        {
            internal System.Action work, apply;
            internal volatile bool finished;
            public System.Exception Error { get; internal set; }
            public bool Finished => finished;
        }

        static readonly object gate = new object();
        static readonly Queue<Job> todo = new Queue<Job>();
        static readonly List<Job> done = new List<Job>();
        static Thread[] threads;
        static int busy;

        /// <summary>Jobs queued, running or waiting for <see cref="Pump"/>.</summary>
        public static int InFlight { get { lock (gate) return todo.Count + busy + done.Count; } }
        /// <summary>Work finished on the workers since the process began, ms.</summary>
        public static double WorkerMs { get; private set; }
        static readonly double ToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        /// <summary>Queue <paramref name="work"/> for a worker and
        /// <paramref name="apply"/> for the main thread after it; not threaded,
        /// both run now, here.</summary>
        public static Job Run(System.Action work, System.Action apply)
        {
            var j = new Job { work = work, apply = apply };
            if (!Threaded)
            {
                try { work(); }
                catch (System.Exception e) { j.Error = e; UnityEngine.Debug.LogException(e); }
                j.finished = true;
                apply?.Invoke();
                return j;
            }
            EnsureThreads();
            lock (gate) { todo.Enqueue(j); Monitor.Pulse(gate); }
            return j;
        }

        /// <summary>The main thread, once a frame: every finished job's apply.</summary>
        public static void Pump()
        {
            if (threads == null) return;
            Job[] ready;
            lock (gate)
            {
                if (done.Count == 0) return;
                ready = done.ToArray();
                done.Clear();
            }
            foreach (var j in ready)
            {
                // a failed job's apply still runs (with nothing made): its caller
                // forgets the ask, so the work is neither lost nor held in flight
                if (j.Error != null) UnityEngine.Debug.LogException(j.Error);
                try { j.apply?.Invoke(); }
                catch (System.Exception e) { UnityEngine.Debug.LogException(e); }
            }
        }

        static void EnsureThreads()
        {
            if (threads != null) return;
            // two at most: the main thread, the render thread and the audio
            // keep theirs; a phone's little cores are no help to a big job
            int n = System.Math.Max(1, System.Math.Min(2, UnityEngine.SystemInfo.processorCount / 4));
            threads = new Thread[n];
            for (int i = 0; i < n; i++)
            {
                threads[i] = new Thread(Loop) { IsBackground = true, Name = "CityWorker" + i, Priority = ThreadPriority.BelowNormal };
                threads[i].Start();
            }
        }

        static void Loop()
        {
            while (true)
            {
                Job j;
                lock (gate)
                {
                    while (todo.Count == 0) Monitor.Wait(gate);
                    j = todo.Dequeue();
                    busy++;
                }
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                try { j.work(); }
                catch (System.Exception e) { j.Error = e; }
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * ToMs;
                lock (gate)
                {
                    busy--;
                    WorkerMs += ms;
                    j.finished = true;
                    done.Add(j);
                }
            }
        }
    }
}
