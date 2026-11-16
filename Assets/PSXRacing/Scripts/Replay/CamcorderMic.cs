using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The CAMCORDER's microphone (see <see cref="Camcorder"/>): the whole
    /// mix, as the AudioListener hears it, turned into a camcorder's sound
    /// track. A filter on the listener's own GameObject (OnAudioFilterRead
    /// sees the finished mix), so it costs one pass over the buffer and no
    /// change to any sound in the game.
    ///
    ///   MONO, from the one capsule on top of the camera.
    ///   THE MIC'S BAND: nothing much under ~150 Hz or over ~6.5 kHz.
    ///   AGC: the automatic gain that pumps - the wind and the room come up
    ///     between cars and duck when one goes by flat out.
    ///   WIND on the mic: low rumble in gusts, worse in a moving car's
    ///     window (<see cref="Wind"/>, set by the director per shot).
    ///   ROOM: two early reflections off whatever stands near.
    ///   HISS of the tape.
    /// The listener already sits where the operator stands (it rides the
    /// camera), so the cars come up and fall away with the distance.
    /// PSX_CAM_MIC=0 leaves the game's own sound.
    /// </summary>
    [RequireComponent(typeof(AudioListener))]
    public class CamcorderMic : MonoBehaviour
    {
        static CamcorderMic instance;

        /// <summary>Wind on the mic, 0..1 (a gusty roadside ~0.35, a car
        /// window ~0.8). Written on the main thread, read on the audio one.</summary>
        public static volatile float Wind = 0.35f;
        /// <summary>The deck's scan: the sound squeals up or groans down.</summary>
        public static volatile float Scan;

        int rate = 48000;
        // Filters.
        float hpX, hpY, lp1, lp2, env = 0.05f, brown, windLp, gust = 0.5f, gustTarget = 0.5f;
        int gustLeft;
        uint rng = 2463534242u;
        float[] delay;
        int dpos;
        float aHp, bLp, bWind, atk, rel;

        /// <summary>Switch the camcorder's sound on for this camera's listener.</summary>
        public static void Attach(Camera cam)
        {
            if (!Camcorder.MicEnabled) return;
            AudioListener listener = cam != null ? cam.GetComponent<AudioListener>() : null;
            if (listener == null) listener = Object.FindFirstObjectByType<AudioListener>();
            if (listener == null) return;
            var mic = listener.GetComponent<CamcorderMic>();
            if (mic == null) mic = listener.gameObject.AddComponent<CamcorderMic>();
            mic.enabled = true;
            instance = mic;
        }

        public static void Detach()
        {
            if (instance != null) instance.enabled = false;
        }

        /// <summary>For the tools: whether the filter is running.</summary>
        public static bool Active => instance != null && instance.isActiveAndEnabled;

        void OnEnable()
        {
            rate = Mathf.Max(8000, AudioSettings.outputSampleRate);
            float dt = 1f / rate;
            aHp = Rc(150f) / (Rc(150f) + dt);
            bLp = dt / (Rc(6500f) + dt);
            bWind = dt / (Rc(260f) + dt);
            atk = 1f - Mathf.Exp(-dt / 0.04f);
            rel = 1f - Mathf.Exp(-dt / 0.9f);
            delay = new float[Mathf.CeilToInt(rate * 0.06f) + 2];
            dpos = 0;
        }

        static float Rc(float hz) => 1f / (2f * Mathf.PI * hz);

        float White()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / 8388608f - 1f;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (delay == null || channels < 1) return;
            int frames = data.Length / channels;
            float wind = Mathf.Clamp01(Wind);
            float scan = Scan;
            int d1 = Mathf.Min(delay.Length - 1, (int)(rate * 0.023f));
            int d2 = Mathf.Min(delay.Length - 1, (int)(rate * 0.041f));
            for (int i = 0; i < frames; i++)
            {
                int o = i * channels;
                float x = 0f;
                for (int c = 0; c < channels; c++) x += data[o + c];
                x /= channels;

                // The mic's band.
                float hp = aHp * (hpY + x - hpX);
                hpX = x; hpY = hp;
                lp1 += bLp * (hp - lp1);
                lp2 += bLp * (lp1 - lp2);
                float s = lp2;

                // The room: two early reflections.
                float wet = delay[(dpos - d1 + delay.Length) % delay.Length] * 0.16f +
                            delay[(dpos - d2 + delay.Length) % delay.Length] * 0.09f;
                delay[dpos] = s;
                dpos = (dpos + 1) % delay.Length;
                s += wet;

                // The AGC: a slow follower that lifts the quiet and ducks the loud.
                float a = Mathf.Abs(s);
                env += (a > env ? atk : rel) * (a - env);
                float gain = Mathf.Clamp(0.22f / Mathf.Max(env, 0.02f), 0.6f, 3.2f);

                // Wind: brown noise in gusts.
                if (--gustLeft <= 0)
                {
                    gustLeft = rate / 6;
                    gustTarget = Mathf.Clamp01(0.5f + White() * 0.5f);
                }
                gust += (gustTarget - gust) * 0.00008f;
                brown = Mathf.Clamp(brown + White() * 0.02f, -1f, 1f) * 0.998f;
                windLp += bWind * (brown - windLp);
                float w = windLp * wind * (0.35f + gust) * 0.5f;

                float hiss = White() * 0.0035f;
                float y = (s + w) * gain + hiss;
                if (scan != 0f) y *= 0.35f;   // the deck mutes most of a scan
                // Soft clip.
                y = y / (1f + Mathf.Abs(y) * 0.6f);
                for (int c = 0; c < channels; c++) data[o + c] = y;
            }
        }
    }
}
