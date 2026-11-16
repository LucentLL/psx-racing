using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CAMCORDER (2026-11-16): the operators' planner on a synthetic ring
    /// (never on the road, never where told not to stand, at a person's eye
    /// height, on both sides of the road), and the OSD's date, clock and
    /// counter words.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        static void TestCamcorder()
        {
            Line("camcorder:");
            Check(Camcorder.DateStamp(320) == "NOV 16 1999", "the stamp's date is the career's, camcorder order", Camcorder.DateStamp(320));
            Check(Camcorder.TimeStamp(21.7f) == "9:42 PM", "and its clock", Camcorder.TimeStamp(21.7f));
            Check(Camcorder.TimeStamp(0.25f) == "12:15 AM" && Camcorder.TimeStamp(12f) == "12:00 PM", "midnight and noon read as a camcorder reads them",
                  Camcorder.TimeStamp(0.25f) + " / " + Camcorder.TimeStamp(12f));
            Check(Camcorder.Counter(83.9f) == "0:01:23" && Camcorder.Counter(3725f) == "1:02:05", "the tape counter", Camcorder.Counter(3725f));

            var tgo = new GameObject("CamcorderRing");
            try
            {
                var path = tgo.AddComponent<TrackPath>();
                int n = 400;
                path.waypoints = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f;
                    path.waypoints[i] = new Vector3(Mathf.Cos(a) * 255f, 0f, Mathf.Sin(a) * 255f);
                }
                path.curvatures = new float[n];
                path.spacing = 4f; path.roadWidth = 9.5f;
                const float half = 9.5f * 0.5f;
                // The ground is road within the half width (plus a shoulder
                // of tarmac a metre wide), grass outside it.
                System.Func<float, float, Vector2> ground = (x, z) =>
                    new Vector2(0f, Mathf.Abs(new Vector2(x, z).magnitude - 255f) < half + 1f ? 1f : 0f);
                var spots = CamcorderCam.Plan(path, 9.5f, ground, (a, b) => false);
                Check(spots.Count >= n * 4 / (int)CamcorderCam.SpotEveryM - 2, "an operator every ~" + CamcorderCam.SpotEveryM + " m", spots.Count);
                bool offRoad = true, eyes = true, inside = false, outside = false;
                foreach (var sp in spots)
                {
                    // (An overpass operator stands on a deck over the road: none
                    // on a ring with nothing over it, unless the open scene has
                    // colliders there - not this test's business.)
                    if (sp.kind == CamcorderCam.Kind.Overpass) continue;
                    float lateral = Mathf.Abs(new Vector2(sp.pos.x, sp.pos.z).magnitude - 255f) - half;
                    if (lateral < 1.5f) offRoad = false;
                    if (Mathf.Abs(sp.pos.y - CamcorderCam.EyeM) > 0.01f && Mathf.Abs(sp.pos.y - CamcorderCam.CrouchM) > 0.01f) eyes = false;
                    if (new Vector2(sp.pos.x, sp.pos.z).magnitude < 255f) inside = true; else outside = true;
                }
                Check(offRoad, "no operator stands on the road (never in a lane)");
                Check(eyes, "every operator's lens is at a person's eye, standing or crouched");
                Check(outside, "they stand on the outside of the corner", inside + "/" + outside);
                // Told to keep off a quarter of the lap (a tunnel), nobody stands there.
                var avoided = CamcorderCam.Plan(path, 9.5f, ground, (a, b) => false, idx => idx < n / 4);
                bool none = true;
                foreach (var sp in avoided) if (sp.idx < n / 4) none = false;
                int Ground(System.Collections.Generic.List<CamcorderCam.Spot> l)
                { int c = 0; foreach (var sp in l) if (sp.kind != CamcorderCam.Kind.Overpass) c++; return c; }
                Check(none && avoided.Count < spots.Count, "no operator where one is told not to stand", avoided.Count);
                // All road: nowhere to stand, nobody planted (the buddy's car films it all).
                var paved = CamcorderCam.Plan(path, 9.5f, (x, z) => new Vector2(0f, 1f), (a, b) => false);
                Check(Ground(paved) == 0, "where every spot is road, nobody is planted", Ground(paved));
                // A gorge (nothing under the spot) or a roof: dropped.
                var gorge = CamcorderCam.Plan(path, 9.5f, (x, z) => new Vector2(float.NaN, 0f), (a, b) => false);
                var roof = CamcorderCam.Plan(path, 9.5f, (x, z) => new Vector2(30f, 0f), (a, b) => false);
                Check(Ground(gorge) == 0 && Ground(roof) == 0, "nobody floats over a gorge or stands on a roof", Ground(gorge) + "/" + Ground(roof));
            }
            finally { Object.DestroyImmediate(tgo); }
        }
    }
}
