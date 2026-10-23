// Browser side of Desync.Net.WebBridge. Every argument is a number (or a string only on rare, user-driven calls):
// the page hook is refreshed ~4x/s from Unity without allocating on the managed side.
//
// window.__DESYNC__ = {
//   ready, state ('title'|'playing'|'paused'|'recap'|'results'|'ending'),
//   metrics: { lastRestartMs, fps }, run: { room, seed },
//   debug: { kill(cause), cmd(line), state() }     // go through unityInstance.SendMessage('DesyncDebug', 'Cmd', ...)
// }
mergeInto(LibraryManager.library, {

  Desync_HookInit: function () {
    var d = window.__DESYNC__ || (window.__DESYNC__ = {});
    d.ready = false;
    d.state = 'title';
    d.metrics = { lastRestartMs: -1, fps: 0 };
    d.run = { room: -1, seed: 0 };
    d._names = ['title', 'playing', 'paused', 'recap', 'results', 'ending'];
    d._stateJson = '';
    var send = function (line) {
      var u = window.unityInstance;   // assigned by the page template once createUnityInstance resolves
      if (u && u.SendMessage) u.SendMessage('DesyncDebug', 'Cmd', line);
      else console.warn('[DESYNC] debug hook: the game instance is not up yet');
    };
    d.debug = {
      // cause: 'ice' | 'melt' | 'trace' | 'glitch'
      kill: function (cause) { send('kill ' + cause); },
      cmd: function (line) { send(String(line)); },
      // Full GameState snapshot as an object (the same JSON the 'state' command logs). SendMessage is synchronous.
      state: function () {
        d._stateJson = '';
        send('state');
        try { return JSON.parse(d._stateJson); } catch (e) { return null; }
      }
    };
  },

  Desync_HookUpdate: function (ready, state, room, seed, restartMs, fps) {
    var d = window.__DESYNC__;
    if (!d || !d.run) return;
    d.ready = ready !== 0;
    d.state = d._names[state] || 'title';
    d.run.room = room;
    d.run.seed = seed >>> 0;               // uint32 seed arrives as a signed int
    d.metrics.lastRestartMs = restartMs;
    d.metrics.fps = fps;
  },

  // Called by DebugBridge's 'state' command so debug.state() has something to return.
  Desync_PushState: function (jsonPtr) {
    var d = window.__DESYNC__;
    if (d) d._stateJson = UTF8ToString(jsonPtr);
  },

  // Clipboard: navigator.clipboard where the page is a secure context, else a hidden textarea + execCommand.
  // The outcome is polled with Desync_CopyStatus (1 pending, 2 copied, 3 refused).
  Desync_CopyText: function (textPtr) {
    var text = UTF8ToString(textPtr);
    window.__desyncCopy = 1;
    var fallback = function () {
      var ok = false;
      try {
        var ta = document.createElement('textarea');
        ta.value = text;
        ta.setAttribute('readonly', '');
        ta.style.cssText = 'position:fixed;top:0;left:0;opacity:0';
        document.body.appendChild(ta);
        ta.select();
        ok = document.execCommand('copy');
        document.body.removeChild(ta);
      } catch (e) { ok = false; }
      var c = document.getElementById('unity-canvas');
      if (c) c.focus();                    // the textarea stole the focus the game needs for keys
      window.__desyncCopy = ok ? 2 : 3;
    };
    if (navigator.clipboard && navigator.clipboard.writeText && window.isSecureContext) {
      navigator.clipboard.writeText(text).then(function () { window.__desyncCopy = 2; }, fallback);
    } else {
      fallback();
    }
  },

  Desync_CopyStatus: function () {
    var s = window.__desyncCopy | 0;
    if (s > 1) window.__desyncCopy = 0;    // result delivered
    return s;
  },

  Desync_DoNotTrack: function () {
    var n = navigator;
    return (n.doNotTrack === '1' || n.doNotTrack === 'yes' || window.doNotTrack === '1' || n.msDoNotTrack === '1') ? 1 : 0;
  }
});
