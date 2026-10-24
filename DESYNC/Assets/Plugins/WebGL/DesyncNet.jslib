// Browser side of Desync.Net.Leaderboard: fetch() against the page's own origin, polled from Unity once a frame.
// DesyncNet_Send returns a request id; DesyncNet_Status(id) is -1 while pending, then the HTTP status (0 = network error or
// timeout); DesyncNet_Take(id) hands over the response text once and forgets the request.
mergeInto(LibraryManager.library, {

  DesyncNet_Send: function (methodPtr, urlPtr, bodyPtr, timeoutMs) {
    var net = window.__desyncNet || (window.__desyncNet = { next: 1, done: {} });
    var id = net.next++;
    var method = UTF8ToString(methodPtr), body = UTF8ToString(bodyPtr);
    var opts = { method: method, cache: 'no-store', headers: {} };
    if (body) { opts.body = body; opts.headers['Content-Type'] = 'application/json'; }
    if (typeof AbortController !== 'undefined') {
      var ctl = new AbortController();
      setTimeout(function () { ctl.abort(); }, timeoutMs);
      opts.signal = ctl.signal;
    }
    fetch(UTF8ToString(urlPtr), opts)
      .then(function (r) { return r.text().then(function (t) { net.done[id] = { status: r.status, text: t }; }); })
      .catch(function () { net.done[id] = { status: 0, text: '' }; });
    return id;
  },

  DesyncNet_Status: function (id) {
    var net = window.__desyncNet, r = net && net.done[id];
    return r ? r.status : -1;
  },

  DesyncNet_Take: function (id) {
    var net = window.__desyncNet, r = net && net.done[id];
    if (r) delete net.done[id];
    var s = r ? r.text : '';
    var size = lengthBytesUTF8(s) + 1, buf = _malloc(size);
    stringToUTF8(s, buf, size);
    return buf;   // freed by the managed string marshaller
  }
});
