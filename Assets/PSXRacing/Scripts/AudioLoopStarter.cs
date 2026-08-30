using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Starts AudioSources only once the platform has actually finished
    /// producing their sample data.
    ///
    /// This exists because of a platform difference that is invisible in the
    /// editor and silent in the browser. On desktop
    /// <see cref="AudioClip.LoadAudioData"/> blocks, so the clip is ready the
    /// moment it returns and `src.clip = c; src.Play();` on the next line
    /// works. On WebGL every clip goes through the browser's
    /// <c>decodeAudioData()</c>, which is ASYNCHRONOUS however the clip was
    /// imported: Unity's audio shim returns the sound handle immediately with a
    /// null buffer, and calling Play() before the decode lands logs
    /// "Trying to play sound which is not loaded", wires an
    /// AudioBufferSourceNode to that null buffer, and plays SILENCE. Nothing
    /// ever retries.
    ///
    /// A looping voice started one frame too early is therefore silent for the
    /// entire session — which is exactly what "there is no sound in game" was
    /// on the deployed build while the editor sounded fine. The engine voice is
    /// the worst case: <see cref="EngineVoiceLibrary.Clip"/> kicks off the load
    /// and <see cref="EngineAudio"/> builds its whole band ladder in the same
    /// frame, so on WebGL every band was guaranteed to lose the race.
    ///
    /// <see cref="AudioClip.loadState"/> is the correct gate: Unity's WebGL
    /// backend reports Loaded only once the decoded buffer exists, so waiting
    /// on it waits on the browser rather than on a guess.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class AudioLoopStarter : MonoBehaviour
    {
        struct Shot
        {
            public AudioSource src;
            public AudioClip clip;
            public float volume;
            public float playAtUnscaled;  // 0 = as soon as it is ready
            public float giveUpAt;
        }

        /// <summary>How long to wait on the decoder before giving up and playing
        /// anyway. A cap is needed or the watch list grows for the whole
        /// session; playing anyway at the end of it is what keeps the worst case
        /// equal to the old unconditional Play() rather than worse than it. If
        /// some future platform never reports Loaded, this degrades to the
        /// previous behaviour instead of to silence.</summary>
        const float GiveUpSeconds = 30f;
        /// <summary>Give-up for a sound that answers a player action rather than
        /// running continuously.</summary>
        const float ReactionGiveUpSeconds = 2f;

        static AudioLoopStarter instance;
        readonly List<AudioSource> loops = new List<AudioSource>();
        readonly List<float> loopGiveUp = new List<float>();
        readonly List<Shot> shots = new List<Shot>();

        static AudioLoopStarter Instance
        {
            get
            {
                if (instance != null) return instance;
                var go = new GameObject("AudioLoopStarter");
                // Survives the scene reload that RESTART RACE does, so a voice
                // still waiting on the browser is not orphaned mid-decode.
                DontDestroyOnLoad(go);
                instance = go.AddComponent<AudioLoopStarter>();
                return instance;
            }
        }

        /// <summary>True on any platform where a clip is ready as soon as it is
        /// loaded, and in edit-mode tooling where spawning a driver object would
        /// be both pointless and rude. Callers fall back to a straight Play().</summary>
        static bool Immediate => !Application.isPlaying;

        static bool Ready(AudioClip clip) =>
            clip != null && clip.loadState == AudioDataLoadState.Loaded;

        /// <summary>
        /// Ask for the sample data if nobody has. Every caller in the game does
        /// already — EngineVoiceLibrary loads on demand, the core clips import
        /// preloaded — but a waiter that depends on its callers having done the
        /// right thing would sit at Unloaded forever if one of them ever
        /// stopped. Loading an already-loading clip is a no-op.
        /// </summary>
        static void Nudge(AudioClip clip)
        {
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
        }

        /// <summary>
        /// Start a looping source as soon as its clip has data. Use this instead
        /// of <c>src.Play()</c> for every always-on, volume-gated loop.
        /// </summary>
        public static void PlayLoop(AudioSource src)
        {
            if (src == null) return;
            if (Immediate || Ready(src.clip)) { StartLoop(src); return; }
            Instance.Watch(src);
        }

        /// <summary>
        /// Repair the seam, then start.
        ///
        /// The takes in the engine pack are not cut on a zero crossing, and at
        /// roughly a second each that is a click a second — see
        /// <see cref="LoopSeam"/>. This is the one place in the game where a
        /// loop is known to have its samples AND to be about to play, which
        /// makes it the only place the repair can happen without every caller
        /// having to remember. Seamless() hands back the original clip whenever
        /// it cannot do better, so nothing here can lose a voice.
        /// </summary>
        static void StartLoop(AudioSource src)
        {
            src.clip = LoopSeam.Seamless(src.clip);
            src.Play();
        }

        /// <summary>
        /// One-shot that must not be dropped if the clip is still decoding — the
        /// startup fire-up, which plays during the countdown a fraction of a
        /// second after the family is selected. <paramref name="delay"/> is
        /// honoured from the moment the clip becomes playable.
        /// </summary>
        public static void PlayDelayed(AudioSource src, AudioClip clip, float delay)
        {
            if (src == null || clip == null) return;
            if (Immediate || Ready(clip))
            {
                src.clip = clip;
                if (delay > 0f) src.PlayDelayed(delay); else src.Play();
                return;
            }
            Instance.WatchShot(new Shot
            {
                src = src,
                clip = clip,
                volume = -1f,                       // -1 = assign to .clip and Play
                playAtUnscaled = delay,
                giveUpAt = Time.unscaledTime + GiveUpSeconds,
            });
        }

        /// <summary>Deferred <see cref="AudioSource.PlayOneShot(AudioClip,float)"/>.</summary>
        public static void PlayOneShot(AudioSource src, AudioClip clip, float volume)
        {
            if (src == null || clip == null) return;
            if (Immediate || Ready(clip)) { src.PlayOneShot(clip, volume); return; }
            Instance.WatchShot(new Shot
            {
                src = src,
                clip = clip,
                volume = Mathf.Max(0f, volume),
                playAtUnscaled = 0f,
                // Much shorter than the loops': a blow-off is a reaction to
                // something the player just did, so one that arrives late is a
                // noise with no cause. Better dropped.
                giveUpAt = Time.unscaledTime + ReactionGiveUpSeconds,
            });
        }

        void Watch(AudioSource src)
        {
            if (loops.Contains(src)) return;
            loops.Add(src);
            loopGiveUp.Add(Time.unscaledTime + GiveUpSeconds);
        }

        void WatchShot(Shot s) => shots.Add(s);

        void Update()
        {
            // While the listener is paused a source reports itself as not
            // playing, and re-issuing Play() would restart every loop from
            // sample zero on resume — the phase-offset artifact the one-source-
            // per-clip design exists to avoid.
            if (AudioListener.pause) return;

            float now = Time.unscaledTime;

            for (int i = loops.Count - 1; i >= 0; i--)
            {
                var src = loops[i];
                if (src == null || src.isPlaying || src.clip == null)
                {
                    loops.RemoveAt(i); loopGiveUp.RemoveAt(i);
                    continue;
                }
                Nudge(src.clip);
                bool expired = now > loopGiveUp[i];

                if (src.clip.loadState == AudioDataLoadState.Failed)
                {
                    loops.RemoveAt(i); loopGiveUp.RemoveAt(i);
                    continue;
                }
                // Keep waiting while the decode is in flight, and while the
                // object is inactive (a car deactivated by the field applier
                // comes back with the scene, not with the decoder).
                if (!expired && (!Ready(src.clip) || !src.isActiveAndEnabled)) continue;

                if (src.isActiveAndEnabled) StartLoop(src);
                loops.RemoveAt(i); loopGiveUp.RemoveAt(i);
            }

            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var s = shots[i];
                if (s.src == null || s.clip == null ||
                    s.clip.loadState == AudioDataLoadState.Failed)
                {
                    shots.RemoveAt(i);
                    continue;
                }
                Nudge(s.clip);
                // One-shots expire rather than firing late — unlike the loops,
                // which play whenever they are ready because a continuous voice
                // has no moment to miss.
                if (now > s.giveUpAt) { shots.RemoveAt(i); continue; }
                if (!Ready(s.clip)) continue;

                if (s.volume < 0f)
                {
                    s.src.clip = s.clip;
                    if (s.playAtUnscaled > 0f) s.src.PlayDelayed(s.playAtUnscaled);
                    else s.src.Play();
                }
                else s.src.PlayOneShot(s.clip, s.volume);
                shots.RemoveAt(i);
            }
        }
    }
}
