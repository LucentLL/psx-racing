using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// NIGHT 4 round 4b: the rays' commands written on the job threads too (the main thread only schedules):
    /// a street lamp's head-to-ground rays (StreetLights.Night4.cs) and a car's fan (CarLights.Occlusion.cs).
    /// </summary>
    public struct N4FieldFillJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector3> heads;
        [ReadOnly] public NativeArray<float> grounds;
        [ReadOnly] public NativeArray<Vector2> dirs;     // the bearings (cos, sin)
        [ReadOnly] public NativeArray<float> radii;
        public QueryParameters qp;
        [WriteOnly] public NativeArray<RaycastCommand> cmds;

        public void Execute(int index)
        {
            int K = radii.Length, per = dirs.Length * K;
            int t = index / per, rem = index - t * per, b = rem / K, k = rem - b * K;
            var h = heads[t];
            var c = dirs[b];
            var target = new Vector3(h.x + c.x * radii[k], grounds[t], h.z + c.y * radii[k]);
            var d = target - h;
            float len = d.magnitude;
            cmds[index] = new RaycastCommand(h, d / Mathf.Max(len, 1e-4f), qp, len + 1.0f);
        }
    }

    public struct N4FanFillJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector3> origins, fwds, rights;
        [ReadOnly] public NativeArray<Vector2> dirs;     // (cos, sin) of each bin's angle off the axis
        public QueryParameters qp;
        public float distance;
        [WriteOnly] public NativeArray<RaycastCommand> cmds;

        public void Execute(int index)
        {
            int bins = dirs.Length, car = index / bins, b = index - car * bins;
            var c = dirs[b];
            cmds[index] = new RaycastCommand(origins[car], fwds[car] * c.x + rights[car] * c.y, qp, distance);
        }
    }
}
