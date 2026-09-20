using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace PSXRacing
{
    /// <summary>
    /// Fullscreen, as a row the player can press.
    ///
    /// The web build already asks for fullscreen once, on the tap that dismisses
    /// the splash. It does not stay: switching tabs, the notification shade,
    /// Back, Escape and — on some phones — a rotation all drop the page out of
    /// it, and a page is not allowed to put itself back. The game then spends
    /// the rest of the session under an address bar and a status bar, with a
    /// third of a phone's screen gone. Reported exactly that way, from a paused
    /// race with the browser chrome across the top of the picture.
    ///
    /// Unlike the other rows in that menu this is NOT a preference the game
    /// owns. <see cref="On"/> asks the browser what is true right now, because
    /// the player can leave fullscreen by routes the game never hears about and
    /// a label saying ON over a visible address bar is worse than no label. The
    /// only thing remembered is the player's last ANSWER, written to
    /// localStorage by the plugin so the template's opening request can skip
    /// itself for somebody who said no (see PSXFullscreen.jslib).
    ///
    /// <see cref="Supported"/> is false where fullscreen cannot happen at all —
    /// an iPhone, or an embed with no fullscreen permission — and the two menus
    /// leave the row out entirely rather than offering a button that cannot
    /// work.
    /// </summary>
    public static class FullscreenPrefs
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int PSXFullscreenSupported();
        [DllImport("__Internal")] static extern int PSXFullscreenActive();
        [DllImport("__Internal")] static extern int PSXFullscreenSet(int on);

        static int supported = -1;
#endif

        /// <summary>What was last asked for, and when. See <see cref="On"/>.</summary>
        static bool desired;
        static float askedAt = -99f;

        /// <summary>Can this platform go fullscreen at all? Asked once: it is a
        /// property of the browser and the frame the game is in, neither of
        /// which changes under a running session.</summary>
        public static bool Supported
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                if (supported < 0)
                {
                    try { supported = PSXFullscreenSupported(); }
                    catch (System.Exception) { supported = 0; }
                }
                return supported != 0;
#else
                // A player with its own window always can, and the editor
                // preview tools need the row to exist to photograph it.
                return true;
#endif
            }
        }

        /// <summary>
        /// Is the picture filling the screen right now?
        ///
        /// FOR A MOMENT AFTER A REQUEST THIS REPORTS WHAT WAS ASKED FOR rather
        /// than what is true. Entering fullscreen is a promise: it settles a
        /// frame or more after the button was pressed, so a label rebuilt in the
        /// same frame — which is exactly what the OPTIONS page does — would read
        /// back the OLD state and the row would look like a dead button that
        /// only works every other press. Half a second later the browser is
        /// asked again and gets the last word, including when it refused.
        /// </summary>
        public static bool On
        {
            get
            {
                if (Time.unscaledTime - askedAt < 0.6f) return desired;
                return Query();
            }
        }

        static bool Query()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { return PSXFullscreenActive() != 0; }
            catch (System.Exception) { return desired; }
#elif UNITY_EDITOR
            // No browser, and putting the editor's own game view fullscreen
            // from a preview harness would be a fine way to lose a batchmode
            // run. The answer is the switch's own position.
            return desired;
#else
            return Screen.fullScreen;
#endif
        }

        public static void Set(bool on)
        {
            desired = on;
            askedAt = Time.unscaledTime;
#if UNITY_WEBGL && !UNITY_EDITOR
            try { PSXFullscreenSet(on ? 1 : 0); }
            catch (System.Exception) { /* no plugin: the row will read what is true */ }
#elif !UNITY_EDITOR
            Screen.fullScreen = on;
#endif
        }

        public static void Toggle() => Set(!On);

        public static string Label => On ? "ON" : "OFF";
    }
}
