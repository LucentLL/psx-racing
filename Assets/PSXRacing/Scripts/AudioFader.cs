using UnityEngine;
using UnityEngine.SceneManagement;

namespace PSXRacing
{
    /// <summary>
    /// The one hand on the master volume.
    ///
    /// Crossing a zone line takes the sound down with the car — engine, tyres,
    /// turbo, the cargo clanging about, all of it — and arriving in the next
    /// zone brings it back up. AudioListener.volume is a single global, which
    /// makes it the right knob and also the dangerous one: it survives a scene
    /// load, so a fade-out that nobody answers is a game gone silent for good.
    ///
    /// Two rules keep that from happening. This object lives across scenes
    /// (DontDestroyOnLoad) so a fade in flight is never orphaned, and EVERY
    /// scene load fades back in unless told to hold — so the worst case of a
    /// forgotten call is a fade-in the player did not ask for, which is what
    /// arriving somewhere sounds like anyway.
    /// </summary>
    public class AudioFader : MonoBehaviour
    {
        static AudioFader inst;
        float target = 1f, speed = 2f;

        /// <summary>Time the sound takes to go, and to come back. Out is
        /// quicker: the line is crossed at speed and the menu is up inside a
        /// third of a second; in is slower, the way a scene fades up.</summary>
        public const float OutSeconds = 0.35f, InSeconds = 0.9f;

        static AudioFader Get()
        {
            if (inst != null) return inst;
            var go = new GameObject("~AudioFader");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<AudioFader>();
            return inst;
        }

        void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            // Arriving anywhere is a fade-in. A menu that faded the sound out
            // and then loaded a scene gets its answer here without having to
            // know what it loaded.
            FadeIn();
        }

        public static void FadeOut()
        {
            var f = Get();
            f.target = 0f;
            f.speed = 1f / OutSeconds;
        }

        public static void FadeIn()
        {
            var f = Get();
            f.target = 1f;
            f.speed = 1f / InSeconds;
        }

        void Update()
        {
            // Unscaled, because the pause menu runs at timeScale zero and a
            // fade that stops with it leaves a paused game half-silent.
            AudioListener.volume = Mathf.MoveTowards(AudioListener.volume, target,
                                                     speed * Time.unscaledDeltaTime);
        }
    }
}
