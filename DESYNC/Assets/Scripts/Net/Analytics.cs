using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Desync.Net
{
    /// Anonymous gameplay events. In a WebGL build every Track() goes to the page (Assets/Plugins/WebGL/DesyncPage.jslib, then the
    /// template's __desyncTrack), which hands it to Vercel Web Analytics as a custom event: window.va('event', { name, data }).
    /// Elsewhere (editor, desktop builds) Track() is a no-op until a provider key is set, and then fills a bounded queue that a
    /// future sender drains with TryDequeue. Nothing is recorded when the browser says Do Not Track. Props must be anonymous
    /// gameplay facts (room id, seed, time, cause): no names, no free text, no raw player id. Every event carries "pid", a
    /// one-way hash of the random profile id, so one player's events can be followed without identifying anyone.
    /// What is sent, and what is not, is written down in docs/ANALYTICS.md.
    public static class Analytics
    {
        public struct Event { public string Name; public (string, object)[] Props; public long UtcMs; }

        const int MaxPending = 256, MaxText = 100;

        /// Set when an account exists (a non-WebGL sender). The queue fills only while this is set and there is no Sink.
        public static string ProviderKey = "";
        public static bool DoNotTrack = WebBridge.DoNotTrack();

        /// Where events go instead of the queue: the page in a WebGL build, a capture in tests. Arguments: event name, JSON props.
        public static Action<string, string> Sink =
#if UNITY_WEBGL && !UNITY_EDITOR
            Desync_Track;
#else
            null;
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void Desync_Track(string name, string json);
#endif

        static readonly Queue<Event> queue = new Queue<Event>();
        static string pid = "", pidFor;

        public static bool Enabled => !DoNotTrack && (Sink != null || !string.IsNullOrEmpty(ProviderKey));
        public static int Pending => queue.Count;

        public static void Track(string name, params (string, object)[] props)
        {
            if (!Enabled) return;
            if (Sink != null) { Sink(name, ToJson(props)); return; }
            if (queue.Count >= MaxPending) queue.Dequeue();
            queue.Enqueue(new Event { Name = name, Props = props, UtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        }

        public static bool TryDequeue(out Event e)
        {
            if (queue.Count == 0) { e = default; return false; }
            e = queue.Dequeue();
            return true;
        }

        public static void Clear() => queue.Clear();

        // ------------------------------------------------------------------ payload
        /// The flat JSON object Vercel Web Analytics takes as event data: strings (cut at 100 characters), numbers and booleans,
        /// no nesting. Always ends with "pid". Public so the payload can be checked without a browser.
        public static string ToJson((string, object)[] props)
        {
            var sb = new StringBuilder(96);
            sb.Append('{');
            bool first = true;
            if (props != null)
                foreach (var (k, v) in props)
                {
                    if (string.IsNullOrEmpty(k) || k == "pid") continue;
                    if (!first) sb.Append(',');
                    first = false;
                    Str(sb, k); sb.Append(':'); Val(sb, v);
                }
            string p = Pid();
            if (p.Length > 0) { if (!first) sb.Append(','); Str(sb, "pid"); sb.Append(':'); Str(sb, p); }
            return sb.Append('}').ToString();
        }

        static void Val(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string s: Str(sb, s); break;
                case float f: Num(sb, f); break;
                case double d: Num(sb, d); break;
                case char c: Str(sb, c.ToString()); break;
                case IFormattable n when v.GetType().IsPrimitive: sb.Append(n.ToString(null, CultureInfo.InvariantCulture)); break;
                default: Str(sb, v.ToString()); break;   // enums and the like: their name
            }
        }

        static void Num(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("null");
            else sb.Append(d.ToString("0.###", CultureInfo.InvariantCulture));
        }

        static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            int n = Math.Min(s.Length, MaxText);
            for (int i = 0; i < n; i++)
            {
                char c = s[i];
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20) sb.Append(' ');
                else sb.Append(c);
            }
            sb.Append('"');
        }

        // ------------------------------------------------------------------ hashed player id
        /// First 12 hex digits of SHA-256("desync:" + the random profile id): one-way, stable for a profile, shared with nobody.
        public static string HashPlayerId(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return "";
            try
            {
                using (var sha = SHA256.Create())
                {
                    var h = sha.ComputeHash(Encoding.UTF8.GetBytes("desync:" + playerId));
                    var sb = new StringBuilder(12);
                    for (int i = 0; i < 6; i++) sb.Append(h[i].ToString("x2", CultureInfo.InvariantCulture));
                    return sb.ToString();
                }
            }
            catch (Exception)   // a runtime without SHA-256: two FNV-1a hashes, still one-way enough for a pseudonym. Analytics must never break the game.
            {
                return Desync.Rules.Rng.HashStr("desync:" + playerId).ToString("x8", CultureInfo.InvariantCulture)
                     + (Desync.Rules.Rng.HashStr("pid:" + playerId) & 0xffff).ToString("x4", CultureInfo.InvariantCulture);
            }
        }

        static string Pid()
        {
            string id = SaveService.Data.playerId;
            if (id != pidFor) { pidFor = id; pid = HashPlayerId(id); }
            return pid;
        }
    }
}
