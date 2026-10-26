/**
 * PSX Racing - the browser's Gamepad API, read and rumbled directly.
 *
 * TWO JOBS, both things Unity's WebGL input layer does not do:
 *
 *  1. RAW CONTROLLERS (Scripts/Controls/RawPads.cs). A wheel, a set of pedals,
 *     a handbrake or a button box is a "non-standard" gamepad to the browser:
 *     navigator.getGamepads() lists it with plain axes[] and buttons[] and no
 *     mapping. The WHEEL SETUP screen captures from these numbers directly, by
 *     device id and index, so nothing depends on how the engine would have
 *     guessed at a layout for it. One call a frame copies every pad into a
 *     float buffer the C# side owns.
 *
 *  2. RUMBLE (Scripts/Controls/Rumble.cs). gamepad.vibrationActuator
 *     .playEffect('dual-rumble', ...) - Chrome and Edge on desktop and
 *     Android; Firefox's older hapticActuators[0].pulse() as a fallback. The
 *     pad is picked by its id (Unity's Gamepad.current product string), or
 *     else the most recently used pad that can rumble at all.
 *
 * Defensive throughout: every browser call is wrapped, and a page with no
 * Gamepad API at all reads as no pads and no rumble rather than an exception
 * inside the player loop.
 */
var PSXGamepadIOLib = {

  $PSXPAD: {
    ids: [],
    gen: 0,
    list: function () {
      try { return navigator.getGamepads ? navigator.getGamepads() : []; } catch (e) { return []; }
    },
    actuatorOf: function (g) {
      if (!g) return null;
      if (g.vibrationActuator) return { va: g.vibrationActuator };
      if (g.hapticActuators && g.hapticActuators.length) return { ha: g.hapticActuators[0] };
      return null;
    },
    pick: function (want) {
      var list = PSXPAD.list(), best = null, bestT = -1;
      for (var i = 0; i < list.length; i++) {
        var g = list[i];
        if (!g || !g.connected) continue;
        var a = PSXPAD.actuatorOf(g);
        if (!a) continue;
        if (want && g.id === want) return a;
        if (want && g.id && want.indexOf(g.id) >= 0) return a;
        if (g.timestamp > bestT) { bestT = g.timestamp; best = a; }
      }
      return best;
    },
  },

  /**
   * Copy every connected pad into buf (float32, cap floats):
   *   [count, then per pad: index, standard(1/0), axes n, buttons n, axes..., button values...]
   * Axes are capped at 16 and buttons at 96 a pad. Returns a generation
   * number that changes whenever the set of ids changes, so the C# side asks
   * for the id strings only then.
   */
  PSXPadPoll: function (buf, cap) {
    var base = buf >> 2, w = 1, n = 0, ids = [];
    var list = PSXPAD.list();
    for (var i = 0; i < list.length; i++) {
      var g = list[i];
      if (!g || !g.connected) continue;
      var na = Math.min(g.axes ? g.axes.length : 0, 16);
      var nb = Math.min(g.buttons ? g.buttons.length : 0, 96);
      if (w + 4 + na + nb > cap) break;
      HEAPF32[base + w] = g.index;
      HEAPF32[base + w + 1] = g.mapping === 'standard' ? 1 : 0;
      HEAPF32[base + w + 2] = na;
      HEAPF32[base + w + 3] = nb;
      w += 4;
      for (var a = 0; a < na; a++) HEAPF32[base + w + a] = +g.axes[a] || 0;
      w += na;
      for (var b = 0; b < nb; b++) {
        var bt = g.buttons[b];
        HEAPF32[base + w + b] = bt ? (typeof bt === 'object' ? (bt.pressed && !(bt.value > 0) ? 1 : +bt.value || 0) : +bt || 0) : 0;
      }
      w += nb;
      ids.push(g.index + ':' + g.id);
      n++;
    }
    HEAPF32[base] = n;
    var key = ids.join('\n');
    if (key !== PSXPAD.ids.join('\n')) { PSXPAD.ids = ids; PSXPAD.gen++; }
    return PSXPAD.gen;
  },

  /** The id string of the pad at browser index `index`, or "". */
  PSXPadId: function (index) {
    var s = '';
    try {
      var list = PSXPAD.list();
      for (var i = 0; i < list.length; i++) if (list[i] && list[i].index === index) { s = list[i].id || ''; break; }
    } catch (e) { s = ''; }
    var size = lengthBytesUTF8(s) + 1;
    var ptr = _malloc(size);
    stringToUTF8(s, ptr, size);
    return ptr;
  },

  /**
   * Rumble the player's pad for `ms` milliseconds (the C# side refreshes it
   * faster than that, so a held rumble is seamless). strong = the low,
   * heavy motor; weak = the high, light one. Both 0 stops it. Returns 1 if a
   * pad that can rumble was found.
   */
  PSXRumble: function (idPtr, strong, weak, ms) {
    try {
      var want = idPtr ? UTF8ToString(idPtr) : '';
      var a = PSXPAD.pick(want);
      if (!a) return 0;
      strong = Math.max(0, Math.min(1, strong)); weak = Math.max(0, Math.min(1, weak));
      ms = Math.max(16, Math.min(5000, ms | 0));
      if (a.va) {
        if (strong <= 0 && weak <= 0) {
          if (a.va.reset) { var r = a.va.reset(); if (r && r['catch']) r['catch'](function () {}); }
          return 1;
        }
        var p = a.va.playEffect('dual-rumble', { startDelay: 0, duration: ms, strongMagnitude: strong, weakMagnitude: weak });
        if (p && p['catch']) p['catch'](function () { /* preempted by the next refresh: expected */ });
        return 1;
      }
      if (a.ha && a.ha.pulse) {
        var q = a.ha.pulse(Math.max(strong, weak), (strong <= 0 && weak <= 0) ? 0 : ms);
        if (q && q['catch']) q['catch'](function () {});
        return 1;
      }
    } catch (e) { /* a pad unplugged mid-call */ }
    return 0;
  },

  /** Stop every pad's motors - pause, focus loss, leaving a drive. */
  PSXRumbleStopAll: function () {
    try {
      var list = PSXPAD.list();
      for (var i = 0; i < list.length; i++) {
        var a = PSXPAD.actuatorOf(list[i]);
        if (!a) continue;
        if (a.va && a.va.reset) { var r = a.va.reset(); if (r && r['catch']) r['catch'](function () {}); }
        else if (a.ha && a.ha.pulse) { var q = a.ha.pulse(0, 0); if (q && q['catch']) q['catch'](function () {}); }
      }
    } catch (e) { }
  },
};

autoAddDeps(PSXGamepadIOLib, '$PSXPAD');
mergeInto(LibraryManager.library, PSXGamepadIOLib);
