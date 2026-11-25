using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// NIGHT 4 round 4b: the rays NIGHT 4 casts on the job threads (the headlight fans, the street-lamp
    /// views) are finished before the physics steps again - first thing in every FixedUpdate - so no
    /// query job ever overlaps a simulation step. The jobs are a fraction of a millisecond and are done by
    /// then; this only makes sure. Their results are still read on the main thread later, a few a frame.
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    public class Night4PhysicsFence : MonoBehaviour
    {
        static Night4PhysicsFence instance;

        public static void Ensure()
        {
            if (instance != null || !Application.isPlaying) return;
            var go = new GameObject("Night4PhysicsFence") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<Night4PhysicsFence>();
        }

        void FixedUpdate()
        {
            CarLights.OccFence();
            StreetLights.N4Fence();
        }
    }
}
