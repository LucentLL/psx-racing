/**
 * PSX Racing — fullscreen, from inside the game.
 *
 * The WebGL template already asks for fullscreen once, on the tap that
 * dismisses the splash, because that tap is a user gesture and a browser will
 * not grant fullscreen without one. What it cannot do is KEEP it: switching
 * tabs, pulling down the notification shade, pressing Back or Escape, and on
 * some phones merely rotating the device all drop the page out of fullscreen,
 * and nothing on the page is allowed to put it back on its own. The game then
 * runs for the rest of the session under an address bar and a status bar —
 * which is the state this was reported in.
 *
 * So the way back has to be a thing the player presses, and the pause menu and
 * OPTIONS are where presses live. See <see cref="PSXRacing.FullscreenPrefs"/>.
 *
 * ABOUT THE GESTURE. A UGUI button is not a DOM button: the browser's pointerup
 * lands in Unity's event queue and the click is dispatched from the next frame's
 * player loop, a task or two later. That still counts, because a gesture grants
 * TRANSIENT ACTIVATION which lasts about five seconds rather than for the
 * duration of the handler — one frame is comfortably inside it. It does mean
 * fullscreen can only ever be reached from a control the player touched, never
 * from a load or a scene change, which is why nothing here runs by itself.
 *
 * The choice is mirrored into localStorage so the template's opening request
 * can honour a player who said no last time. localStorage rather than
 * PlayerPrefs because the template is plain page script that runs before the
 * engine exists, and Unity keeps its prefs in IndexedDB where the page cannot
 * reach them.
 */
var PSXFullscreenLib = {

  $PSXFS: {
    KEY: 'psx.fullscreen',
    hooked: false,

    /** Whole page, not the canvas alone — the same element the template asks
     *  for, so the two cannot end up fighting over which one is fullscreen. */
    el: function () { return document.documentElement; },

    supported: function () {
      try {
        // False in an iframe without allow="fullscreen", and undefined on an
        // iPhone, where Safari has element fullscreen for <video> and nothing
        // else. Either way there is no point offering the row.
        if (document.fullscreenEnabled === false ||
            document.webkitFullscreenEnabled === false) return false;
        var e = PSXFS.el();
        return !!(e && (e.requestFullscreen || e.webkitRequestFullscreen));
      } catch (err) { return false; }
    },

    active: function () {
      try {
        return !!(document.fullscreenElement || document.webkitFullscreenElement);
      } catch (err) { return false; }
    },

    remember: function (on) {
      try { localStorage.setItem(PSXFS.KEY, on ? '1' : '0'); } catch (err) { /* private mode */ }
    },

    /**
     * Make the canvas re-measure whenever the viewport changes under it.
     *
     * The template sizes the canvas from a `resize` listener, and Unity follows
     * the canvas (matchWebGLToCanvasSize). Entering fullscreen normally fires
     * resize on its own, but not always in time and not always once: Android
     * Chrome retracts the URL bar over a couple of hundred milliseconds AFTER
     * the fullscreen transition, so a single early measurement leaves the game
     * rendering into a viewport two bars short and the HUD parked off the
     * bottom edge. Two beats cost nothing and cover both.
     */
    hook: function () {
      if (PSXFS.hooked) return;
      PSXFS.hooked = true;
      var bump = function () {
        setTimeout(function () { window.dispatchEvent(new Event('resize')); }, 0);
        setTimeout(function () { window.dispatchEvent(new Event('resize')); }, 350);
      };
      document.addEventListener('fullscreenchange', bump);
      document.addEventListener('webkitfullscreenchange', bump);
    },
  },

  /** 1 if this browser will ever grant fullscreen. Asked once and cached C#-side. */
  PSXFullscreenSupported: function () { return PSXFS.supported() ? 1 : 0; },

  /** 1 while the page IS fullscreen. The browser is the source of truth here —
   *  the player can leave fullscreen by routes the game never hears about. */
  PSXFullscreenActive: function () { return PSXFS.active() ? 1 : 0; },

  /**
   * Ask to enter or leave. Returns 1 if the request was made at all; whether it
   * was GRANTED is answered later by PSXFullscreenActive, because both calls
   * are promises that settle after this frame.
   */
  PSXFullscreenSet: function (on) {
    PSXFS.hook();
    PSXFS.remember(!!on);
    try {
      if (on) {
        var e = PSXFS.el();
        if (!e.requestFullscreen && !e.webkitRequestFullscreen) return 0;
        // navigationUI only on the standard call: the webkit one predates the
        // options argument and took a flag constant in that position.
        var p = e.requestFullscreen ? e.requestFullscreen({ navigationUI: 'hide' })
                                    : e.webkitRequestFullscreen();
        if (p && p.then) {
          // Landscape is what a driving game wants, and the lock is only
          // permitted once fullscreen is actually granted — so it waits.
          // Rejects on every desktop browser; that is not a failure.
          p.then(function () {
            try {
              if (screen.orientation && screen.orientation.lock) {
                var q = screen.orientation.lock('landscape');
                if (q && q['catch']) q['catch'](function () {});
              }
            } catch (err) { /* no orientation API */ }
          })['catch'](function () { /* user or browser declined */ });
        }
      } else {
        var ex = document.exitFullscreen || document.webkitExitFullscreen;
        if (!ex) return 0;
        var r = ex.call(document);
        if (r && r['catch']) r['catch'](function () {});
      }
      return 1;
    } catch (err) { return 0; }
  },
};

autoAddDeps(PSXFullscreenLib, '$PSXFS');
mergeInto(LibraryManager.library, PSXFullscreenLib);
