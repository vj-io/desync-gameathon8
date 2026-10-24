// Page hooks added for launch: analytics events and the autoplay status. The page template (WebGLTemplates/Desync/index.html) owns the
// other side: window.__desyncTrack(name, json) forwards to Vercel Web Analytics, window.__DESYNC__.autoplay is read by tests/e2e.
mergeInto(LibraryManager.library, {

  // Desync.Net.Analytics.Sink in a WebGL build: one anonymous event (flat JSON props, see docs/ANALYTICS.md).
  Desync_Track: function (namePtr, jsonPtr) {
    var f = window.__desyncTrack;
    if (typeof f !== 'function') return;
    try { f(UTF8ToString(namePtr), UTF8ToString(jsonPtr)); } catch (e) { /* analytics must never break the game */ }
  },

  // Desync.Flow.Autoplay (?autoplay=1): state 0 idle, 1 running, 2 done, 3 failed; phase = RunPhase; rooms cleared; won 0/1; act; run seed.
  Desync_AutoplayStatus: function (state, phase, rooms, won, act, seed) {
    var d = window.__DESYNC__;
    if (!d) return;
    d.autoplay = { state: ['idle', 'running', 'done', 'failed'][state] || 'idle', phase: phase, rooms: rooms, won: won !== 0, act: act, seed: seed >>> 0 };
  }
});
