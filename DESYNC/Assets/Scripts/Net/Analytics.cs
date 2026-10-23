using System;
using System.Collections.Generic;

namespace Desync.Net
{
    /// Event sink with no provider yet (D-007): Track() is a no-op until ProviderKey is set, and never records anything when the
    /// browser says Do Not Track. Props must be anonymous gameplay facts (room id, seed, time): no names, no player id, no free text.
    public static class Analytics
    {
        public struct Event { public string Name; public (string, object)[] Props; public long UtcMs; }

        const int MaxPending = 256;

        /// Set when an account exists. A future sender drains the queue with TryDequeue.
        public static string ProviderKey = "";
        public static bool DoNotTrack = WebBridge.DoNotTrack();

        static readonly Queue<Event> queue = new Queue<Event>();

        public static bool Enabled => !string.IsNullOrEmpty(ProviderKey) && !DoNotTrack;
        public static int Pending => queue.Count;

        public static void Track(string name, params (string, object)[] props)
        {
            if (!Enabled) return;
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
    }
}
