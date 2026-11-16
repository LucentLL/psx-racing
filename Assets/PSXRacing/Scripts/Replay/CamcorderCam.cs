using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The CAMCORDER replay's camera work (see <see cref="Camcorder"/>).
    /// Placeholder surface; the director lands in the next commit.
    /// </summary>
    public class CamcorderCam : MonoBehaviour
    {
        TrackPath path;
        Camera cam;
        float roadWidth = 12f;
        System.Func<int, bool> avoid;

        public void Setup(TrackPath p, Camera c, float roadWidthM, System.Func<int, bool> avoidAt = null)
        {
            path = p; cam = c; roadWidth = roadWidthM; avoid = avoidAt;
        }

        public void Retarget() { }
    }
}
