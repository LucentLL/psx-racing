// The colour harness's way out of the player (Scripts/Dev/ShotLink.cs): a
// captured frame as window.__psxShot = { name, png (base64), n } and a POST
// of the PNG bytes to __psxshot?name=... beside the page, which
// tools/colour/shot-link.mjs writes to disk. Only a development player
// served from this machine, asked by URL, ever calls this.
mergeInto(LibraryManager.library, {
  PSXShotLink_Post: function (namePtr, dataPtr, length) {
    var name = UTF8ToString(namePtr);
    var bytes = new Uint8Array(HEAPU8.buffer, dataPtr, length).slice();
    var s = "";
    for (var i = 0; i < bytes.length; i += 0x8000) {
      s += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
    }
    var n = (window.__psxShot && window.__psxShot.n) ? window.__psxShot.n + 1 : 1;
    window.__psxShot = { name: name, png: btoa(s), n: n };
    window.__psxShots = window.__psxShots || [];
    window.__psxShots.push(name);
    try {
      fetch("__psxshot?name=" + encodeURIComponent(name), { method: "POST", body: bytes })
        .then(function (r) { console.log("[ShotLink] POSTED " + name + " " + r.status); })
        .catch(function (e) { console.log("[ShotLink] POST FAILED " + name + " " + e); });
    } catch (e) { console.log("[ShotLink] POST FAILED " + name + " " + e); }
  },
  // The frame's STATE line ("name {json}") and the end of the list, for a
  // server that cannot read the console (a real-GPU browser, served).
  PSXShotLink_Note: function (kindPtr, textPtr) {
    var kind = UTF8ToString(kindPtr), text = UTF8ToString(textPtr);
    try { fetch(kind === "done" ? "__psxdone" : "__psxstate", { method: "POST", body: text }).catch(function () {}); } catch (e) {}
  }
});
