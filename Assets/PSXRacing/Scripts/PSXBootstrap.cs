using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Per-platform runtime setup: frame pacing, screen sleep, and physics rate.
    /// Kept separate from the scene builder so behaviour can change without a rebuild.
    /// </summary>
    public class PSXBootstrap : MonoBehaviour
    {
        public int targetFrameRate = 60;
        public float fixedTimestep = 1f / 60f;

        void Awake()
        {
            Application.targetFrameRate = targetFrameRate;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            QualitySettings.vSyncCount = 0;

            // 60 Hz physics keeps the tire model stable without costing a phone too much.
            Time.fixedDeltaTime = fixedTimestep;
            Time.maximumDeltaTime = 0.1f;

            // Shadows and extra lights are already off in the PSX shaders; make sure
            // the pipeline is not paying for them on mobile either.
            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.skinWeights = SkinWeights.OneBone;

            if (Application.isMobilePlatform)
                Screen.orientation = ScreenOrientation.AutoRotation;
        }
    }
}
