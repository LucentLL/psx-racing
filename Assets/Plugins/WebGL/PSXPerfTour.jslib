// The free-roam perf tour's way out of the player (Scripts/Dev/PerfTour.cs):
// a route's frames, spikes and summary (or a shot's PNG) POSTed to
// __psxperf?name=... beside the page, which tools/perf-tour.mjs writes to
// disk. Only a player served from this machine, asked by URL, ever calls it.
mergeInto(LibraryManager.library, {
  PSXPerfTour_Post: function (namePtr, dataPtr, length) {
    var name = UTF8ToString(namePtr);
    var bytes = new Uint8Array(HEAPU8.buffer, dataPtr, length).slice();
    try {
      fetch("__psxperf?name=" + encodeURIComponent(name), { method: "POST", body: bytes })
        .then(function (r) { console.log("[PerfTour] POSTED " + name + " " + r.status); })
        .catch(function (e) { console.log("[PerfTour] POST FAILED " + name + " " + e); });
    } catch (e) { console.log("[PerfTour] POST FAILED " + name + " " + e); }
  }
});
