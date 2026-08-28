/**
 * PSX Racing — master tone chain for the Web build.
 *
 * AudioToneChain does its work in OnAudioFilterRead on the AudioListener, which
 * on every other platform hands it the summed mix. The Web player has no such
 * callback: Unity's Web audio backend is a thin wrapper over WebAudio that
 * exposes only create/play/stop/volume/pitch/pan, wires every AudioSource
 * straight to audioContext.destination, and creates no DSP nodes at all
 * (grep the shipped framework: zero createScriptProcessor, zero AudioWorklet,
 * zero createBiquadFilter). So the whole chain — 9 dB of tilt between the
 * engine fundamental and the whine band, plus the saturation — silently
 * evaporated in the browser and nowhere else. That is why the deployed build
 * sounded thin against the same content in the editor.
 *
 * This rebuilds the identical chain out of WebAudio nodes and splices it in
 * front of the destination.
 *
 * Two deliberate choices:
 *
 *  - IIRFilterNode, not BiquadFilterNode. The C# side computes RBJ cookbook
 *    coefficients with a shelf slope of 0.707; WebAudio's own shelf nodes are
 *    hardwired to S = 1 and would land somewhere near but not on the same
 *    curve. Passing the coefficients across keeps the browser and the editor
 *    bit-comparable, which is the whole point of having one set of numbers.
 *
 *  - The splice is a wrapper on AudioNode.prototype.connect rather than a hook
 *    into Unity's internals. Nothing here depends on the shape of WEBAudio, so
 *    an editor upgrade that renames its fields cannot silently switch the
 *    chain off again — which is exactly the failure being fixed.
 */
var PSXToneChainLib = {

  $PSXTone: {
    ctx: null,
    input: null,
    output: null,
    patched: false,
    origConnect: null,
    cfg: null,

    /** tanh(x * drive) * trim, sampled for a WaveShaperNode. */
    curve: function (drive, trim) {
      var n = 2048, c = new Float32Array(n);
      for (var i = 0; i < n; i++) {
        var x = (i / (n - 1)) * 2 - 1;
        var e = Math.exp(2 * x * drive);
        c[i] = ((e - 1) / (e + 1)) * trim;
      }
      return c;
    },

    /** Build (or rebuild) the chain on a context. Returns true if audible. */
    build: function (ctx) {
      var cfg = PSXTone.cfg;
      if (!ctx || !cfg) return false;
      try {
        var nodes = [];
        for (var i = 0; i < cfg.stages.length; i++) {
          var s = cfg.stages[i];
          // Feedback array is [1, a1, a2] — the C# side already normalised by a0.
          nodes.push(ctx.createIIRFilter([s[0], s[1], s[2]], [1, s[3], s[4]]));
        }
        var shaper = ctx.createWaveShaper();
        shaper.curve = PSXTone.curve(cfg.drive, cfg.trim);
        shaper.oversample = '2x';
        nodes.push(shaper);
        for (var j = 0; j < nodes.length - 1; j++) {
          PSXTone.rawConnect(nodes[j], nodes[j + 1]);
        }
        var out = nodes[nodes.length - 1];
        // Marked so the wrapper below lets OUR tail reach the speakers instead
        // of looping the chain back into its own input.
        out.__psxToneTail = true;
        PSXTone.rawConnect(out, ctx.destination);

        // A rebuild (a new car's formant) must not orphan sources already
        // pointing at the old input, so the previous head keeps feeding the new
        // one for the moment it takes the old nodes to be collected.
        if (PSXTone.input && PSXTone.ctx === ctx) {
          try { PSXTone.rawConnect(PSXTone.input, nodes[0]); } catch (e) { /* gone */ }
          try { PSXTone.output.disconnect(); } catch (e) { /* gone */ }
        }
        PSXTone.ctx = ctx;
        PSXTone.input = nodes[0];
        PSXTone.output = out;
        return true;
      } catch (e) {
        console.warn('[PSXTone] build failed, running dry:', e);
        PSXTone.input = null;
        return false;
      }
    },

    rawConnect: function (from, to) {
      return (PSXTone.origConnect || AudioNode.prototype.connect).call(from, to);
    },

    /** Route everything bound for the speakers through the chain instead. */
    patch: function () {
      if (PSXTone.patched || typeof AudioNode === 'undefined') return;
      PSXTone.origConnect = AudioNode.prototype.connect;
      AudioNode.prototype.connect = function (dest) {
        try {
          if (dest && dest === dest.context.destination && !this.__psxToneTail) {
            if (PSXTone.ctx !== dest.context) PSXTone.build(dest.context);
            if (PSXTone.input) {
              return arguments.length > 1
                ? PSXTone.origConnect.call(this, PSXTone.input, arguments[1], arguments[2])
                : PSXTone.origConnect.call(this, PSXTone.input);
            }
          }
        } catch (e) { /* fall through to the plain connect */ }
        return PSXTone.origConnect.apply(this, arguments);
      };
      PSXTone.patched = true;
    },
  },

  /**
   * Install or re-tune the chain. `json` is
   *   { stages: [[b0,b1,b2,a1,a2], ...], drive: f, trim: f }
   * with the coefficients already normalised by a0, in signal order.
   *
   * Returns the context's SAMPLE RATE once a chain is live, or 0 while no
   * AudioContext exists yet (the caller retries — the context may not be up
   * until the first user gesture). The rate is the return value rather than a
   * bare success flag because the coefficients are cooked against a sample rate
   * on the C# side, and AudioSettings.outputSampleRate is not always what the
   * browser hands back; the caller compares and re-cooks if they differ, which
   * is the difference between a 200 Hz shelf and a 218 Hz one.
   */
  PSXToneChainInstall: function (json) {
    try {
      PSXTone.cfg = JSON.parse(UTF8ToString(json));
    } catch (e) {
      console.warn('[PSXTone] bad config:', e);
      return 0;
    }
    PSXTone.patch();
    // Adopt a context that already exists, so sources that connected before
    // this call get re-pointed on their next connect and the chain is live
    // for everything created from here on.
    var ctx = null;
    try { if (typeof WEBAudio !== 'undefined') ctx = WEBAudio.audioContext; } catch (e) { /* not Unity's */ }
    if (!ctx) return 0;
    return PSXTone.build(ctx) ? (ctx.sampleRate | 0) : 0;
  },
};

autoAddDeps(PSXToneChainLib, '$PSXTone');
mergeInto(LibraryManager.library, PSXToneChainLib);
