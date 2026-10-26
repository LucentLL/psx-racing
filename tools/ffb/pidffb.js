/*
 * PSX Racing - HID PID force feedback over WebHID (the USB "Physical Interface
 * Device" class, HID PID 1.0, usage page 0x0F).
 *
 * ONE SOURCE. The standalone test page (webhid-ffb-test.html) loads this file
 * as a <script>; the game's Assets/Plugins/WebGL/PSXForceFeedback.jslib carries
 * a copy of the same text between its PIDFFB-BEGIN / PIDFFB-END markers, put
 * there by tools/ffb/sync-jslib.py. Edit HERE, then run the sync.
 *
 * Plain ES5 + Promises on purpose: the jslib copy is parsed by the emscripten
 * linker, and nothing here may depend on a newer parser than the one it ships.
 *
 * HOW A PID DEVICE IS DRIVEN (the order hid-pidff.c in Linux uses, which is
 * what every Moza base is known to work with - Linux's hid-universal-pidff
 * lists the Moza R3/R5/R9/R12/R16/R21 with ONE quirk: conditional effects
 * (spring, damper, friction, inertia) must be sent with the direction fixed at
 * EAST = 90 degrees, i.e. 0x4000 of 0xFFFF):
 *   reset:   Device Control  RESET, RESET, STOP ALL, ENABLE ACTUATORS; Device Gain max
 *   create:  feature Create New Effect (type) -> GET feature PID Block Load
 *            (effect block index + status SUCCESS/FULL/ERROR)
 *   define:  output Set Effect (index, type, duration, gain, direction)
 *            + its type block: Set Constant Force / Set Condition / Set Periodic
 *   play:    output Effect Operation (index, START, loop 1)
 *   stop:    output Effect Operation (index, STOP); Block Free (index)
 *
 * NOTHING ABOUT THE LAYOUT IS ASSUMED. Report IDs, field order, sizes and
 * ranges all come from the device's own report descriptor, which WebHID hands
 * over parsed (device.collections). Chrome appends every report item to its
 * own collection AND every ancestor, so a top-level collection's report lists
 * every item of that report in declaration order - that list is the layout.
 * The sub-collections (Set Effect 0x21, Direction 0x57, Effect Type 0x25 ...)
 * say what the items MEAN; they are matched back onto the top-level list in
 * order (see annotate).
 */
(function (root) {
  'use strict';
  if (root.PSXPidFfb) return;

  var PAGE_GD = 0x01, PAGE_PID = 0x0F;
  var U = {
    SET_EFFECT: 0x21, EBI: 0x22, PBO: 0x23, EFFECT_TYPE: 0x25,
    ET_CONSTANT: 0x26, ET_RAMP: 0x27, ET_SQUARE: 0x30, ET_SINE: 0x31, ET_TRIANGLE: 0x32,
    ET_SAW_UP: 0x33, ET_SAW_DOWN: 0x34, ET_SPRING: 0x40, ET_DAMPER: 0x41, ET_INERTIA: 0x42, ET_FRICTION: 0x43,
    DURATION: 0x50, SAMPLE_PERIOD: 0x51, GAIN: 0x52, TRIGGER_BUTTON: 0x53, TRIGGER_REPEAT: 0x54,
    AXES_ENABLE: 0x55, DIRECTION_ENABLE: 0x56, DIRECTION: 0x57, TYPE_SPECIFIC_OFFSET: 0x58,
    SET_ENVELOPE: 0x5A, SET_CONDITION: 0x5F, CP_OFFSET: 0x60, POS_COEF: 0x61, NEG_COEF: 0x62,
    POS_SAT: 0x63, NEG_SAT: 0x64, DEAD_BAND: 0x65,
    SET_PERIODIC: 0x6E, OFFSET: 0x6F, MAGNITUDE: 0x70, PHASE: 0x71, PERIOD: 0x72,
    SET_CONSTANT: 0x73, SET_RAMP: 0x74,
    EFFECT_OP_REPORT: 0x77, EFFECT_OP: 0x78, OP_START: 0x79, OP_START_SOLO: 0x7A, OP_STOP: 0x7B, LOOP_COUNT: 0x7C,
    DEVICE_GAIN_REPORT: 0x7D, DEVICE_GAIN: 0x7E,
    POOL_REPORT: 0x7F, RAM_POOL_SIZE: 0x80, SIMULTANEOUS_MAX: 0x83, DEVICE_MANAGED_POOL: 0xA9,
    BLOCK_LOAD_REPORT: 0x89, BLOCK_LOAD_STATUS: 0x8B, BL_SUCCESS: 0x8C, BL_FULL: 0x8D, BL_ERROR: 0x8E,
    BLOCK_FREE_REPORT: 0x90, STATE_REPORT: 0x92,
    DEVICE_CONTROL_REPORT: 0x95, DEVICE_CONTROL: 0x96, DC_ENABLE: 0x97, DC_DISABLE: 0x98,
    DC_STOP_ALL: 0x99, DC_RESET: 0x9A, DC_PAUSE: 0x9B, DC_CONTINUE: 0x9C,
    START_DELAY: 0xA7, CREATE_NEW_EFFECT: 0xAB, RAM_POOL_AVAILABLE: 0xAC
  };
  var NAMES = {};
  for (var k in U) if (Object.prototype.hasOwnProperty.call(U, k)) NAMES[U[k]] = k;
  var REPORT_USAGES = [U.SET_EFFECT, U.SET_ENVELOPE, U.SET_CONDITION, U.SET_PERIODIC, U.SET_CONSTANT,
    U.SET_RAMP, U.EFFECT_OP_REPORT, U.DEVICE_GAIN_REPORT, U.POOL_REPORT, U.BLOCK_LOAD_REPORT,
    U.BLOCK_FREE_REPORT, U.STATE_REPORT, U.DEVICE_CONTROL_REPORT, U.DEVICE_CONTROL, U.CREATE_NEW_EFFECT];

  function ext(page, id) { return (((page & 0xFFFF) * 65536) + (id & 0xFFFF)); }
  function pid(id) { return ext(PAGE_PID, id); }
  function hex(n, w) { var s = (n >>> 0).toString(16).toUpperCase(); while (s.length < (w || 2)) s = '0' + s; return s; }
  function usageName(u) {
    var page = Math.floor(u / 65536), id = u & 0xFFFF;
    if (page === PAGE_PID) return (NAMES[id] || ('PID_' + hex(id))) + '(0F:' + hex(id) + ')';
    if (page === PAGE_GD) {
      var gd = { 0x30: 'X', 0x31: 'Y', 0x32: 'Z', 0x33: 'Rx', 0x34: 'Ry', 0x35: 'Rz', 0x36: 'Slider', 0x37: 'Dial', 0x38: 'Wheel', 0x39: 'Hat', 0x04: 'Joystick', 0x05: 'Gamepad', 0x3B: 'ByteCount' };
      return (gd[id] || 'GD') + '(01:' + hex(id) + ')';
    }
    if (page === 0x0A) return 'Ordinal' + id;
    if (page === 0x09) return 'Button' + id;
    return hex(page, 4) + ':' + hex(id, 4);
  }
  function bytesHex(b) { var s = []; for (var i = 0; i < b.length; i++) s.push(hex(b[i])); return s.join(' '); }

  function itemUsages(it) {
    var out = [];
    if (it.isRange) {
      for (var u = it.usageMinimum; u <= it.usageMaximum && out.length < 512; u++) out.push(u);
    } else if (it.usages) {
      for (var i = 0; i < it.usages.length; i++) out.push(it.usages[i]);
    }
    return out;
  }
  function itemKey(it) {
    return [itemUsages(it).join(','), it.reportSize, it.reportCount, it.logicalMinimum, it.logicalMaximum,
      it.isArray ? 1 : 0, it.isConstant ? 1 : 0, it.isRange ? 1 : 0].join('|');
  }
  /** HID's unit exponent is a signed nibble; Chrome may pass it raw (0x0D = -3). */
  function unitExp(it) { var e = it.unitExponent || 0; if (e > 7) e -= 16; return e; }

  // ---- the layout ----------------------------------------------------------

  /** One report of one top-level collection: items in declaration order with
   *  their bit offsets, and (after annotate) the PID collection each sits in. */
  function buildReports(top) {
    var reps = { input: {}, output: {}, feature: {} };
    function take(kind, list) {
      (list || []).forEach(function (r) {
        var off = 0, entries = [];
        (r.items || []).forEach(function (it) {
          var e = { item: it, bitOffset: off, usages: itemUsages(it), ctx: [] };
          entries.push(e);
          off += (it.reportSize || 0) * (it.reportCount || 0);
        });
        reps[kind][r.reportId] = { id: r.reportId, kind: kind, entries: entries, bits: off, names: [] };
      });
    }
    take('input', top.inputReports);
    take('output', top.outputReports);
    take('feature', top.featureReports);
    return reps;
  }

  /** Every descendant collection: the PID usages it stands for are written onto
   *  the matching top-level items (an order-preserving match on each item's
   *  shape), deeper collections last so they win. */
  function annotate(top, reps) {
    var found = {};   // PID report-collection usage -> { kind, id }
    function visit(col, path) {
      var cu = ext(col.usagePage, col.usage);
      var here = path.concat([cu]);
      if (col.usagePage === PAGE_PID && REPORT_USAGES.indexOf(col.usage) >= 0) {
        [['output', col.outputReports], ['feature', col.featureReports], ['input', col.inputReports]].forEach(function (p) {
          (p[1] || []).forEach(function (r) {
            if (!found[col.usage]) found[col.usage] = { kind: p[0], id: r.reportId };
            var rep = reps[p[0]][r.reportId];
            if (rep && rep.names.indexOf(NAMES[col.usage]) < 0) rep.names.push(NAMES[col.usage]);
          });
        });
      }
      [['output', col.outputReports], ['feature', col.featureReports], ['input', col.inputReports]].forEach(function (p) {
        (p[1] || []).forEach(function (r) {
          var rep = reps[p[0]][r.reportId];
          if (!rep) return;
          var j = 0;
          (r.items || []).forEach(function (it) {
            var key = itemKey(it);
            while (j < rep.entries.length && itemKey(rep.entries[j].item) !== key) j++;
            if (j < rep.entries.length) { rep.entries[j].ctx = here; j++; }
          });
        });
      });
      (col.children || []).forEach(function (c) { visit(c, here); });
    }
    (top.children || []).forEach(function (c) { visit(c, [ext(top.usagePage, top.usage)]); });
    return found;
  }

  function inCtx(e, usage) { return e.ctx.indexOf(pid(usage)) >= 0; }

  /** The field carrying a PID usage directly: { e, j } or null. */
  function field(rep, usage, ctxUsage) {
    if (!rep) return null;
    var want = pid(usage);
    for (var i = 0; i < rep.entries.length; i++) {
      var e = rep.entries[i];
      if (e.item.isConstant || e.item.isArray) continue;
      if (ctxUsage && !inCtx(e, ctxUsage)) continue;
      for (var j = 0; j < e.item.reportCount; j++) {
        var u = e.usages.length ? e.usages[Math.min(j, e.usages.length - 1)] : -1;
        if (u === want) return { e: e, j: j };
      }
    }
    return null;
  }
  /** Every field inside a PID logical collection (Direction, Axes Enable). */
  function fieldsIn(rep, ctxUsage) {
    var out = [];
    if (!rep) return out;
    rep.entries.forEach(function (e) {
      if (e.item.isConstant || !inCtx(e, ctxUsage)) return;
      for (var j = 0; j < e.item.reportCount; j++)
        out.push({ e: e, j: j, usage: e.usages.length ? e.usages[Math.min(j, e.usages.length - 1)] : -1 });
    });
    return out;
  }
  /** An ARRAY item whose usage list holds one of `usages`: { e, values } where
   *  values[u] is the number to write to select usage u. */
  function arrayOf(rep, usages) {
    if (!rep) return null;
    for (var i = 0; i < rep.entries.length; i++) {
      var e = rep.entries[i];
      if (!e.item.isArray) continue;
      var vals = {}, hit = false;
      for (var n = 0; n < e.usages.length; n++) {
        var id = e.usages[n] & 0xFFFF;
        if (Math.floor(e.usages[n] / 65536) !== PAGE_PID) continue;
        if (usages.indexOf(id) >= 0) hit = true;
        vals[id] = e.item.logicalMinimum + n;
      }
      if (hit) return { e: e, values: vals };
    }
    return null;
  }
  /** The same set as a bitmask of 1-bit variable fields (a Device Control some
   *  firmware declares as variables rather than an array). */
  function bitsOf(rep, usages) {
    var out = {}, any = false;
    usages.forEach(function (u) { var f = field(rep, u); if (f) { out[u] = f; any = true; } });
    return any ? out : null;
  }

  // ---- writing -------------------------------------------------------------

  function writer(rep) { return { rep: rep, bytes: new Uint8Array(Math.ceil(rep.bits / 8)), notes: [] }; }
  function putBits(bytes, off, size, v) {
    v = Math.round(v);
    if (v < 0) v += Math.pow(2, size);
    for (var b = 0; b < size; b++) {
      var bit = Math.floor(v / Math.pow(2, b)) % 2;
      var at = off + b;
      if (bit) bytes[at >> 3] |= (1 << (at & 7)); else bytes[at >> 3] &= ~(1 << (at & 7));
    }
  }
  function clampTo(it, v) { return Math.max(it.logicalMinimum, Math.min(it.logicalMaximum, v)); }
  function set(w, f, v, raw) {
    if (!f) return false;
    var it = f.e.item;
    var val = raw ? v : clampTo(it, v);
    putBits(w.bytes, f.e.bitOffset + f.j * it.reportSize, it.reportSize, val);
    w.notes.push(usageName(f.usage !== undefined ? f.usage : (f.e.usages[Math.min(f.j, f.e.usages.length - 1)] || 0)) + '=' + Math.round(val));
    return true;
  }
  function setArray(w, a, usage) {
    if (!a || a.values[usage] === undefined) return false;
    putBits(w.bytes, a.e.bitOffset, a.e.item.reportSize, a.values[usage]);
    w.notes.push(NAMES[usage] + '(' + a.values[usage] + ')');
    return true;
  }
  /** -1..1 onto a signed field (or 0..1 onto an unsigned one). */
  function signedScale(f, x) {
    if (!f) return 0;
    var it = f.e.item;
    if (it.logicalMinimum < 0) return Math.round(x * Math.min(-it.logicalMinimum, it.logicalMaximum));
    return Math.round(Math.abs(x) * it.logicalMaximum);
  }
  function unsignedScale(f, x) {
    if (!f) return 0;
    var it = f.e.item, lo = Math.max(0, it.logicalMinimum);
    return Math.round(lo + Math.max(0, Math.min(1, x)) * (it.logicalMaximum - lo));
  }
  /** Milliseconds in the field's own time unit (Linux's pidff_rescale_time).
   *  An exponent of 0 is read as milliseconds: descriptors that declare no
   *  unit at all are, in practice, milliseconds. */
  function timeScale(f, ms) {
    if (!f) return 0;
    var e = unitExp(f.e.item);
    var v = ms;
    if (e !== 0) { for (; e < -3; e++) v *= 10; for (; e > -3; e--) v /= 10; }
    return Math.round(v);
  }

  // ---- the device ----------------------------------------------------------

  function Pid(device, log) {
    this.device = device;
    this.log = log || function () {};
    this.q = Promise.resolve();
    this.top = null;
    this.reps = null;
    this.found = {};
    this.directionDeg = 90;      // EAST: the Moza quirk, and a sane default for any wheel
    this.axesEnableX = true;
    this.directionEnable = true;
    this.useBlockLoad = true;
    this.nextFixedIndex = 1;
    this.onInput = null;
    this.inputCounts = {};
  }

  Pid.prototype.describe = function () {
    var d = this.device, lines = [];
    lines.push('DEVICE "' + d.productName + '" vendor 0x' + hex(d.vendorId, 4) + ' product 0x' + hex(d.productId, 4) +
      (d.vendorId === 0x346E ? '  (MOZA)' : ''));
    (d.collections || []).forEach(function (c, ci) {
      lines.push('COLLECTION ' + ci + ' ' + usageName(ext(c.usagePage, c.usage)) + ' type ' + c.type);
      function sub(col, depth) {
        (col.children || []).forEach(function (ch) {
          var ids = [];
          ['inputReports', 'outputReports', 'featureReports'].forEach(function (k) {
            (ch[k] || []).forEach(function (r) { ids.push(k.charAt(0) + r.reportId); });
          });
          lines.push(new Array(depth + 2).join('  ') + '- ' + usageName(ext(ch.usagePage, ch.usage)) + ' [' + ids.join(' ') + ']');
          if (depth < 6) sub(ch, depth + 1);
        });
      }
      sub(c, 1);
      ['inputReports', 'outputReports', 'featureReports'].forEach(function (k) {
        (c[k] || []).forEach(function (r) {
          var bits = 0;
          var parts = (r.items || []).map(function (it) {
            bits += it.reportSize * it.reportCount;
            var us = itemUsages(it);
            var name = it.isConstant ? 'pad' : (us.length ? us.slice(0, 4).map(usageName).join('/') + (us.length > 4 ? '/+' + (us.length - 4) : '') : '?');
            return name + ' ' + it.reportSize + 'x' + it.reportCount + (it.isArray ? ' arr' : '') +
              ' [' + it.logicalMinimum + '..' + it.logicalMaximum + ']' + (unitExp(it) ? ' e' + unitExp(it) : '');
          });
          lines.push('  ' + k.replace('Reports', '').toUpperCase() + ' id ' + r.reportId + ' (' + Math.ceil(bits / 8) + ' bytes): ' + parts.join('; '));
        });
      });
    });
    return lines.join('\n');
  };

  /** Pick the top-level collection that carries the PID reports. */
  Pid.prototype.parse = function () {
    var self = this, best = null, bestN = -1;
    (this.device.collections || []).forEach(function (c) {
      var reps = buildReports(c), found = annotate(c, reps), n = Object.keys(found).length;
      if (n > bestN) { best = { top: c, reps: reps, found: found }; bestN = n; }
    });
    if (!best) return false;
    this.top = best.top; this.reps = best.reps; this.found = best.found;
    var names = Object.keys(this.found).map(function (u) {
      var f = self.found[u]; return NAMES[u] + '=' + f.kind.charAt(0) + f.id;
    });
    this.log('PID reports: ' + (names.length ? names.join(' ') : 'NONE'));
    return this.has(U.SET_EFFECT) && this.has(U.EFFECT_OP_REPORT);
  };
  Pid.prototype.has = function (u) { return !!this.found[u]; };
  Pid.prototype.rep = function (u) {
    var f = this.found[u];
    if (f) return this.reps[f.kind][f.id];
    // A report collection the descriptor did not name: look for the report
    // holding its signature field instead.
    var sig = { 0x7D: U.DEVICE_GAIN, 0x77: U.LOOP_COUNT };
    if (sig[u]) {
      for (var kind in this.reps) for (var id in this.reps[kind]) {
        if (field(this.reps[kind][id], sig[u])) return this.reps[kind][id];
      }
    }
    return null;
  };

  /** Queue a send; every report goes out in order and nothing overlaps. */
  Pid.prototype.enqueue = function (fn) {
    var self = this;
    var p = this.q.then(fn);
    this.q = p['catch'](function (e) { self.log('ERROR ' + (e && e.message ? e.message : e)); });
    return p;
  };
  Pid.prototype.sendOut = function (w, label) {
    var self = this, dev = this.device, rep = w.rep;
    return this.enqueue(function () {
      self.log('OUT  ' + label + ' id ' + rep.id + ' [' + bytesHex(w.bytes) + ']  ' + w.notes.join(' '));
      return dev.sendReport(rep.id, w.bytes);
    });
  };
  Pid.prototype.sendFeature = function (w, label) {
    var self = this, dev = this.device, rep = w.rep;
    return this.enqueue(function () {
      self.log('FEAT ' + label + ' id ' + rep.id + ' [' + bytesHex(w.bytes) + ']  ' + w.notes.join(' '));
      return dev.sendFeatureReport(rep.id, w.bytes);
    });
  };
  /** A feature report back from the device, as bytes WITHOUT its report ID. */
  Pid.prototype.readFeature = function (rep, label) {
    var self = this, dev = this.device;
    return this.enqueue(function () {
      return dev.receiveFeatureReport(rep.id).then(function (dv) {
        var b = new Uint8Array(dv.buffer, dv.byteOffset, dv.byteLength);
        var need = Math.ceil(rep.bits / 8);
        if (rep.id !== 0 && b.length >= need + 1 && b[0] === rep.id) b = b.subarray(1);
        self.log('GET  ' + label + ' id ' + rep.id + ' [' + bytesHex(b) + ']');
        return b;
      });
    });
  };
  function readField(bytes, f) {
    if (!f) return null;
    var it = f.e.item, off = f.e.bitOffset + f.j * it.reportSize, v = 0;
    for (var b = 0; b < it.reportSize; b++) {
      var at = off + b;
      if ((at >> 3) < bytes.length && (bytes[at >> 3] >> (at & 7)) & 1) v += Math.pow(2, b);
    }
    if (it.logicalMinimum < 0 && v >= Math.pow(2, it.reportSize - 1)) v -= Math.pow(2, it.reportSize);
    return v;
  }
  function readArray(bytes, a) {
    if (!a) return null;
    var it = a.e.item, off = a.e.bitOffset, v = 0;
    for (var b = 0; b < it.reportSize; b++) {
      var at = off + b;
      if ((at >> 3) < bytes.length && (bytes[at >> 3] >> (at & 7)) & 1) v += Math.pow(2, b);
    }
    for (var u in a.values) if (a.values[u] === v) return parseInt(u, 10);
    return -v;
  }

  Pid.prototype.open = function () {
    var self = this, d = this.device;
    var p = d.opened ? Promise.resolve() : d.open();
    return p.then(function () {
      d.addEventListener('inputreport', function (ev) {
        var n = (self.inputCounts[ev.reportId] || 0) + 1;
        self.inputCounts[ev.reportId] = n;
        var st = self.found[U.STATE_REPORT];
        if (st && st.kind === 'input' && st.id === ev.reportId && n <= 3) {
          var b = new Uint8Array(ev.data.buffer, ev.data.byteOffset, ev.data.byteLength);
          self.log('IN   PID state id ' + ev.reportId + ' [' + bytesHex(b) + ']');
        }
        if (self.onInput) self.onInput(ev);
      });
      self.log('opened "' + d.productName + '"');
    });
  };

  Pid.prototype.pool = function () {
    var rep = this.rep(U.POOL_REPORT);
    if (!rep || rep.kind !== 'feature') { this.log('no PID Pool report (fine: most wheels manage their own pool)'); return Promise.resolve(null); }
    var self = this;
    return this.readFeature(rep, 'PID Pool').then(function (b) {
      var out = {
        ram: readField(b, field(rep, U.RAM_POOL_SIZE)),
        simultaneous: readField(b, field(rep, U.SIMULTANEOUS_MAX)),
        deviceManaged: readField(b, field(rep, U.DEVICE_MANAGED_POOL))
      };
      self.log('pool: RAM ' + out.ram + ', simultaneous effects ' + out.simultaneous + ', device managed ' + out.deviceManaged);
      return out;
    }, function (e) { self.log('PID Pool read failed: ' + e); return null; });
  };

  Pid.prototype.control = function (usage) {
    var rep = this.rep(U.DEVICE_CONTROL) || this.rep(U.DEVICE_CONTROL_REPORT);
    if (!rep) { this.log('no Device Control report - skipped ' + NAMES[usage]); return Promise.resolve(); }
    var w = writer(rep);
    var all = [U.DC_ENABLE, U.DC_DISABLE, U.DC_STOP_ALL, U.DC_RESET, U.DC_PAUSE, U.DC_CONTINUE];
    var a = arrayOf(rep, all);
    if (a) setArray(w, a, usage);
    else {
      var bits = bitsOf(rep, all);
      if (!bits || !bits[usage]) { this.log('Device Control has no ' + NAMES[usage]); return Promise.resolve(); }
      set(w, bits[usage], 1);
    }
    return this.sendOut(w, 'Device Control ' + NAMES[usage]);
  };

  Pid.prototype.gain = function (frac) {
    var rep = this.rep(U.DEVICE_GAIN_REPORT);
    if (!rep) { this.log('no Device Gain report'); return Promise.resolve(); }
    var w = writer(rep), f = field(rep, U.DEVICE_GAIN);
    set(w, f, unsignedScale(f, frac));
    return this.sendOut(w, 'Device Gain');
  };

  /** RESET twice, STOP ALL, ENABLE ACTUATORS, gain - the Linux init order. */
  Pid.prototype.reset = function (gainFrac) {
    var self = this;
    this.nextFixedIndex = 1;
    return this.control(U.DC_RESET)
      .then(function () { return self.control(U.DC_RESET); })
      .then(function () { return self.control(U.DC_STOP_ALL); })
      .then(function () { return self.control(U.DC_ENABLE); })
      .then(function () { return self.gain(gainFrac === undefined ? 1 : gainFrac); });
  };

  /** Ask the device for an effect slot. Resolves the effect block index. */
  Pid.prototype.create = function (typeUsage) {
    var self = this;
    var cne = this.rep(U.CREATE_NEW_EFFECT), bl = this.rep(U.BLOCK_LOAD_REPORT);
    if (!cne || !bl || !this.useBlockLoad) {
      var idx = this.nextFixedIndex++;
      this.log('no Create New Effect / Block Load: using fixed block index ' + idx);
      return Promise.resolve(idx);
    }
    var w = writer(cne);
    var types = [U.ET_CONSTANT, U.ET_RAMP, U.ET_SQUARE, U.ET_SINE, U.ET_TRIANGLE, U.ET_SAW_UP, U.ET_SAW_DOWN,
      U.ET_SPRING, U.ET_DAMPER, U.ET_INERTIA, U.ET_FRICTION];
    if (!setArray(w, arrayOf(cne, types), typeUsage)) self.log('Create New Effect: type ' + NAMES[typeUsage] + ' not offered by this device');
    var send = cne.kind === 'feature' ? this.sendFeature(w, 'Create New Effect ' + NAMES[typeUsage])
                                      : this.sendOut(w, 'Create New Effect ' + NAMES[typeUsage]);
    return send.then(function () {
      if (bl.kind !== 'feature') throw new Error('Block Load is not a feature report (kind ' + bl.kind + ')');
      return self.readFeature(bl, 'Block Load');
    }).then(function (b) {
      var ebi = readField(b, field(bl, U.EBI));
      var st = readArray(b, arrayOf(bl, [U.BL_SUCCESS, U.BL_FULL, U.BL_ERROR]));
      self.log('block load: index ' + ebi + ' status ' + (NAMES[st] || st));
      if (st === U.BL_FULL) throw new Error('device effect memory FULL');
      if (st === U.BL_ERROR) throw new Error('device reported an ERROR creating the effect');
      if (!ebi) throw new Error('device returned effect block index 0');
      return ebi;
    });
  };

  /** Set Effect: durationMs 0 = infinite (all ones, as Linux sends). */
  Pid.prototype.setEffect = function (ebi, typeUsage, durationMs, gainFrac) {
    var rep = this.rep(U.SET_EFFECT), w = writer(rep);
    set(w, field(rep, U.EBI), ebi);
    var types = [U.ET_CONSTANT, U.ET_RAMP, U.ET_SQUARE, U.ET_SINE, U.ET_TRIANGLE, U.ET_SAW_UP, U.ET_SAW_DOWN,
      U.ET_SPRING, U.ET_DAMPER, U.ET_INERTIA, U.ET_FRICTION];
    setArray(w, arrayOf(rep, types), typeUsage);
    var fd = field(rep, U.DURATION);
    if (fd) {
      if (!durationMs) set(w, fd, Math.pow(2, fd.e.item.reportSize) - 1, true);
      else set(w, fd, timeScale(fd, durationMs));
    }
    set(w, field(rep, U.TRIGGER_REPEAT), 0);
    set(w, field(rep, U.SAMPLE_PERIOD), 0);
    var fg = field(rep, U.GAIN);
    set(w, fg, unsignedScale(fg, gainFrac === undefined ? 1 : gainFrac));
    // No trigger button: 0 is outside a 1-based range, so it is the null state.
    var ft = field(rep, U.TRIGGER_BUTTON);
    if (ft) set(w, ft, 0, true);
    var self = this;
    fieldsIn(rep, U.AXES_ENABLE).forEach(function (f, i) { set(w, f, (i === 0 && self.axesEnableX) ? 1 : 0); });
    set(w, field(rep, U.DIRECTION_ENABLE), this.directionEnable ? 1 : 0);
    var dirs = fieldsIn(rep, U.DIRECTION);
    dirs.forEach(function (f, i) {
      var it = f.e.item;
      var deg = i === 0 ? self.directionDeg : 0;
      set(w, f, it.logicalMinimum + Math.round((deg / 360) * (it.logicalMaximum - it.logicalMinimum)));
    });
    if (!dirs.length) this.log('Set Effect has no Direction collection');
    set(w, field(rep, U.START_DELAY), 0);
    return this.sendOut(w, 'Set Effect ' + NAMES[typeUsage]);
  };

  Pid.prototype.setConstant = function (ebi, mag) {
    var rep = this.rep(U.SET_CONSTANT);
    if (!rep) return Promise.reject(new Error('no Set Constant Force report'));
    var w = writer(rep), f = field(rep, U.MAGNITUDE);
    set(w, field(rep, U.EBI), ebi);
    set(w, f, signedScale(f, Math.max(-1, Math.min(1, mag))));
    return this.sendOut(w, 'Set Constant Force');
  };

  /** coef -1..1 (stiffness / damping), sat 0..1, all for axis 0. */
  Pid.prototype.setCondition = function (ebi, coef, sat, deadBand) {
    var rep = this.rep(U.SET_CONDITION);
    if (!rep) return Promise.reject(new Error('no Set Condition report'));
    var w = writer(rep);
    set(w, field(rep, U.EBI), ebi);
    set(w, field(rep, U.PBO), 0);
    fieldsIn(rep, U.TYPE_SPECIFIC_OFFSET).forEach(function (f) { set(w, f, 0); });
    set(w, field(rep, U.CP_OFFSET), 0);
    var pc = field(rep, U.POS_COEF), nc = field(rep, U.NEG_COEF);
    set(w, pc, signedScale(pc, coef));
    set(w, nc, signedScale(nc, coef));
    var ps = field(rep, U.POS_SAT), ns = field(rep, U.NEG_SAT);
    set(w, ps, unsignedScale(ps, sat));
    set(w, ns, unsignedScale(ns, sat));
    var db = field(rep, U.DEAD_BAND);
    set(w, db, unsignedScale(db, deadBand || 0));
    return this.sendOut(w, 'Set Condition');
  };

  Pid.prototype.setPeriodic = function (ebi, mag, periodMs) {
    var rep = this.rep(U.SET_PERIODIC);
    if (!rep) return Promise.reject(new Error('no Set Periodic report'));
    var w = writer(rep), fm = field(rep, U.MAGNITUDE);
    set(w, field(rep, U.EBI), ebi);
    set(w, fm, unsignedScale(fm, Math.abs(mag)));
    set(w, field(rep, U.OFFSET), 0);
    set(w, field(rep, U.PHASE), 0);
    var fp = field(rep, U.PERIOD);
    set(w, fp, timeScale(fp, periodMs));
    return this.sendOut(w, 'Set Periodic');
  };

  Pid.prototype.operate = function (ebi, opUsage, loops) {
    var rep = this.rep(U.EFFECT_OP_REPORT), w = writer(rep);
    set(w, field(rep, U.EBI), ebi);
    setArray(w, arrayOf(rep, [U.OP_START, U.OP_START_SOLO, U.OP_STOP]), opUsage);
    set(w, field(rep, U.LOOP_COUNT), loops === undefined ? 1 : loops);
    return this.sendOut(w, 'Effect Operation ' + NAMES[opUsage]);
  };

  Pid.prototype.free = function (ebi) {
    var rep = this.rep(U.BLOCK_FREE_REPORT);
    if (!rep) return Promise.resolve();
    var w = writer(rep);
    set(w, field(rep, U.EBI), ebi);
    return this.sendOut(w, 'Block Free');
  };

  root.PSXPidFfb = { Pid: Pid, U: U, NAMES: NAMES, hex: hex, usageName: usageName, bytesHex: bytesHex,
    // What requestDevice should offer: joysticks and gamepads, and Moza by name.
    filters: [{ usagePage: 0x01, usage: 0x04 }, { usagePage: 0x01, usage: 0x05 }, { vendorId: 0x346E }] };
})(typeof window !== 'undefined' ? window : this);
