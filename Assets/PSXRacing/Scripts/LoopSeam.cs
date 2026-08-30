using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Makes a looping recording actually loop.
    ///
    /// THE TICK. Reported as "an audible clipping sound approximately every
    /// second", and that interval is the whole diagnosis: every band take in
    /// the engine pack is between 0.79 s and 1.29 s long, they all play at
    /// once, and each one is pitched to somewhere near its own home RPM — so
    /// while driving there are five or six loops wrapping every 1.0 to 1.4
    /// seconds. Nothing else in the game has a one-second period.
    ///
    /// The takes are not cut on a zero crossing and were never meant to be:
    /// measured across rotary_x7, the jump from the last sample to the first
    /// runs to 8% of full scale on very_high_on and 20% on intake_off — bigger
    /// than any transient INSIDE those clips. A step discontinuity is broadband
    /// and the engine under it is tonal, so it reads as a click over the top
    /// rather than as part of the note. The on- and off-throttle takes of a
    /// band are the same length and play at the same rate, so their clicks land
    /// together and the tick is louder than either.
    ///
    /// The repair is the standard one for an auto-looped sample, in two halves:
    ///
    ///   FIND THE CUT. Search a few milliseconds of the tail for the offset
    ///   whose waveform best correlates with the head. Crossfading two copies
    ///   of a tonal signal that are half a cycle apart cancels the fundamental
    ///   for the length of the fade — trading a click for a dropout — and
    ///   aligning first is what stops that.
    ///
    ///   FOLD IT BACK. Shorten the clip to the chosen loop length L and blend
    ///   the discarded tail onto the head over a raised cosine. The weights are
    ///   exact at both ends, so the last sample of the new clip is d[L-1] and
    ///   the first is d[L] — a pair that were already adjacent in the
    ///   recording. The seam is then continuous by construction rather than by
    ///   how carefully the pack was cut.
    ///
    /// Doing it here rather than to the files on disk is deliberate: the source
    /// takes are already Vorbis and Unity re-encodes them again on import, and
    /// a third generation to bake a 12 ms crossfade in would cost more than the
    /// crossfade is worth. It also means a family that is never driven never
    /// pays. The cost is one extra decompressed copy of each loop that actually
    /// gets played — the originals stay resident, because unloading an asset
    /// something else might still one-shot is a trade of a tick for silence.
    ///
    /// Every looping voice in the game starts through
    /// <see cref="AudioLoopStarter.PlayLoop"/>, which already knows the moment
    /// a clip's samples exist (see its notes on the browser's async decode), so
    /// that is the one place this is called from and no caller has to remember.
    /// </summary>
    public static class LoopSeam
    {
        /// <summary>Crossfade length. Long enough to hide a step, short enough
        /// that the brief comb from summing a signal with a delayed copy of
        /// itself is not a sound in its own right on broadband engine noise.
        /// </summary>
        const float FadeSeconds = 0.012f;

        /// <summary>
        /// How far back from the nominal cut to look for a better aligned one.
        ///
        /// Measured over rotary_x7's eighteen takes, this is the number that
        /// matters: at 8 ms the crossfade still lands out of phase often enough
        /// to cost a mean 1.3 dB and a worst 6.1 dB through the blend — a
        /// dropout where the click used to be. At 35 ms it is 0.3 dB mean and
        /// 1.6 dB worst. Widening it further buys nothing; no clip in the pack
        /// chose an offset near the edge of this window.
        /// </summary>
        const float SearchSeconds = 0.035f;

        /// <summary>Shortest clip worth touching. Below this the fade would be
        /// a meaningful fraction of the loop.</summary>
        const int MinSamples = 4096;

        static readonly Dictionary<AudioClip, AudioClip> cache =
            new Dictionary<AudioClip, AudioClip>();

        /// <summary>
        /// A seam-repaired copy of <paramref name="src"/>, or <paramref
        /// name="src"/> itself if it cannot be repaired.
        ///
        /// Never returns null and never returns a silent clip: every failure
        /// path hands back the original, so the worst case is exactly the
        /// behaviour before this existed rather than an engine that has gone
        /// quiet. Cached by source clip, so the four cars sharing a family pay
        /// for one copy.
        /// </summary>
        public static AudioClip Seamless(AudioClip src)
        {
            if (src == null) return null;
            if (cache.TryGetValue(src, out var hit))
            {
                // Three different answers, and C#'s ?? cannot tell them apart —
                // it does not go through Unity's == override, so a clip
                // DESTROYED when the editor left play mode comes back as a
                // perfectly good-looking reference to nothing. A real null is a
                // cached refusal; a fake null is a stale entry to rebuild.
                if (ReferenceEquals(hit, null)) return src;
                if (hit != null) return hit;
                cache.Remove(src);
            }
            // Not decoded yet: answer with the original and do NOT cache, so
            // the next caller (or the next frame) can still get the repair.
            if (src.loadState != AudioDataLoadState.Loaded) return src;

            var made = Build(src);
            cache[src] = made;
            return made != null ? made : src;
        }

        static AudioClip Build(AudioClip src)
        {
            int n = src.samples, ch = src.channels, freq = src.frequency;
            if (n < MinSamples || ch < 1 || freq < 8000) return null;

            var data = new float[n * ch];
            if (!src.GetData(data, 0)) return null;

            int fade = Mathf.Clamp(Mathf.RoundToInt(FadeSeconds * freq), 128, n / 8);
            int search = Mathf.Clamp(Mathf.RoundToInt(SearchSeconds * freq), 0,
                                     n - fade * 3);
            if (fade < 32) return null;

            // A clip whose samples came back empty is one the platform said was
            // loaded and was not. Building from it would replace a working
            // voice with silence, which is far worse than the tick.
            float energy = 0f;
            for (int i = 0; i < data.Length; i += 97) energy += data[i] * data[i];
            if (energy <= 1e-9f) return null;

            int cut = BestCut(data, n, ch, fade, search);

            var outData = new float[cut * ch];
            System.Array.Copy(data, 0, outData, 0, cut * ch);
            for (int i = 0; i < fade; i++)
            {
                // w(0) = 0 and w(fade) = 1, so out[0] is exactly d[cut] and
                // out[fade] is exactly d[fade]: both ends of the blend are the
                // recording itself, and the loop point falls between two
                // samples that were already neighbours.
                float w = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * i / fade);
                int head = i * ch, tail = (cut + i) * ch;
                for (int c = 0; c < ch; c++)
                    outData[head + c] = data[head + c] * w + data[tail + c] * (1f - w);
            }

            var clip = AudioClip.Create(src.name + "_seam", cut, ch, freq, false);
            if (clip == null) return null;
            if (!clip.SetData(outData, 0))
            {
                // Destroy, not DestroyImmediate, is an error outside play mode —
                // and the preview tools build clusters and cars there.
                if (Application.isPlaying) Object.Destroy(clip);
                else Object.DestroyImmediate(clip);
                return null;
            }
            return clip;
        }

        /// <summary>
        /// Where to cut the loop: the offset in the tail whose next
        /// <paramref name="fade"/> samples look most like the first ones.
        ///
        /// Normalised by the candidate's own energy, so a quiet stretch of tail
        /// cannot win simply by being quiet — without that the search settles
        /// on near-silence, which correlates weakly with everything and
        /// crossfades to a hole.
        /// </summary>
        static int BestCut(float[] d, int n, int ch, int fade, int search)
        {
            int nominal = n - fade;
            if (search <= 0) return nominal;

            int best = nominal;
            float bestScore = float.NegativeInfinity;
            for (int cut = nominal - search; cut <= nominal; cut++)
            {
                float dot = 0f, mag = 1e-9f;
                // Every eighth sample. Correlation against a 12 ms window is
                // smooth at this scale — measured over the whole rotary_x7
                // family, striding 8 instead of 4 picks the SAME cut on 16 of
                // 18 takes and lands within two samples on the other two, for
                // half the work. It is worth halving: a family is eighteen
                // clips repaired in whatever frame the browser finishes
                // decoding them, which is a frame during the countdown.
                for (int i = 0; i < fade; i += 8)
                {
                    int a = i * ch, b = (cut + i) * ch;
                    for (int c = 0; c < ch; c++)
                    {
                        dot += d[a + c] * d[b + c];
                        mag += d[b + c] * d[b + c];
                    }
                }
                float score = dot / Mathf.Sqrt(mag);
                if (score > bestScore) { bestScore = score; best = cut; }
            }
            return best;
        }
    }
}
