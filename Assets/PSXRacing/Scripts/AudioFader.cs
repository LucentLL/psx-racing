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

        /// <summary>THE VCR's scan (2026-11-16, TapePlayback): the sound off at
        /// once and back at once - a deck mutes a search, it does not fade it.
        /// Setting AudioListener.volume alone was pulled straight back up by
        /// Update (the tape check's "with the sound off" failed).</summary>
        public static void Mute(bool on)
        {
            var f = Get();
            f.target = on ? 0f : 1f;
            f.speed = 100f;
        }

        void Update()
        {
            // Unscaled, because the pause menu runs at timeScale zero and a
            // fade that stops with it leaves a paused game half-silent.
            // The player's MASTER volume (settings, AUDIO) multiplies the
            // fade, so a zone line still takes the sound to nothing and
            // brings it back to the level the player chose, not to full.
            MasterOut.Level = Mathf.MoveTowards(MasterOut.Level, target * AudioPrefs.Master,
                                               speed * Time.unscaledDeltaTime);
        }

        /// <summary>(PC70) A test run stays silent whatever wrote the listener this frame.</summary>
        void LateUpdate()
        {
            MasterOut.Hold();
        }

        /// <summary>
        /// The master level from the very first frame: the fader used to come
        /// into being on the first zone line, and until then nothing held
        /// AudioListener.volume at all.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Get();
            MasterOut.Level = AudioPrefs.Master;
        }

        /// <summary>A new MASTER level, at once rather than at fade speed
        /// (unless a fade is under way, which carries it on its way).</summary>
        public static void ApplyMaster()
        {
            // Edit mode (the self-test, the preview tools): no fader to make -
            // DontDestroyOnLoad is play-mode only - and no sound to set.
            if (!Application.isPlaying) return;
            var f = Get();
            if (f.target >= 1f) MasterOut.Level = AudioPrefs.Master;
        }
    }

    /// <summary>
    /// (PC70, owner 2026-11-25: "please mute all testing. I constantly hear the game without ever seeing it".)
    /// The master level: every writer sets it here and every check reads it here, in place of
    /// AudioListener.volume. In a test run the level stays what the game set and the listener itself is held
    /// at zero, so the game behaves and costs exactly what it does with the sound on (the mix still runs)
    /// and nothing reaches the speakers.
    ///
    /// A test run is batch mode (every hidden tour, LAB check and editor play check) or -psx-mute on a
    /// visible one. -psx-sound puts the sound back for a run that is meant to be heard.
    ///
    /// Nothing else may write AudioListener.volume: in a test run a direct write is pulled back to zero in
    /// AudioFader.LateUpdate (a frame late: it can be heard), and in the game the next fade overwrites it.
    /// </summary>
    public static class MasterOut
    {
        /// <summary>True in a test run: the listener is held at zero.</summary>
        public static bool Muted { get; private set; }
        static float level = 1f;

        /// <summary>What the game asked for, 0..1. The listener gets it, or zero in a test run.</summary>
        public static float Level
        {
            get => level;
            set { level = value; AudioListener.volume = Muted ? 0f : value; }
        }

        /// <summary>The backstop for a writer that went round <see cref="Level"/>.</summary>
        public static void Hold()
        {
            if (Muted && AudioListener.volume != 0f) AudioListener.volume = 0f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Boot()
        {
            bool mute = Application.isBatchMode, sound = false;
            foreach (var a in System.Environment.GetCommandLineArgs())
            {
                if (a == "-psx-mute") mute = true;
                else if (a == "-psx-sound") sound = true;
            }
            Muted = mute && !sound;
            level = 1f;
            if (!Muted) return;
            AudioListener.volume = 0f;
            Debug.Log("[MasterOut] test run: the sound is muted (" + (Application.isBatchMode ? "batch mode" : "-psx-mute") + ")");
        }
    }
}
