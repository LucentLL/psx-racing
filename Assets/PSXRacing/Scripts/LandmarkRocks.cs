using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE LINN COVE GRANITE's switch (owner 2026-11-16: "would also be nice to
    /// add some of the iconic rock face to the track and the title shot angle").
    /// On the root of a stage's landmark rocks (PSXRacingBuilder.LandmarkRocks:
    /// the boulder field under and above the viaduct, from the 2018 lidar):
    /// PSX_LINNCOVE_ROCKS=0 switches the root off at load - the rocks and their
    /// colliders with it. Tools call <see cref="ApplyAll"/> (Awake never runs in
    /// edit mode).
    /// </summary>
    public class LandmarkRocks : MonoBehaviour
    {
        void Awake()
        {
            if (!LookChoices.LinnCoveRocks) gameObject.SetActive(false);
        }

        /// <summary>Every landmark root in the open scenes to the switch's state.</summary>
        public static int ApplyAll()
        {
            int n = 0;
            foreach (var r in FindObjectsByType<LandmarkRocks>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                r.gameObject.SetActive(LookChoices.LinnCoveRocks);
                n++;
            }
            return n;
        }
    }
}
