using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace Desync.TestKit
{
    /// Exact allocation counts for the zero-GC tests. GC.GetAllocatedBytesForCurrentThread reads 0 under Unity's Mono and
    /// GC.GetTotalMemory moves in whole heap blocks, so both pass whatever the code does; this counts the GC.Alloc profiler
    /// samples made on this thread during one call (the instrument behind Is.Not.AllocatingGCMemory).
    ///
    /// In the editor TextMeshPro keeps a debug copy of every text set through SetText(format, ...), SetText(StringBuilder) or
    /// SetCharArray (#if UNITY_EDITOR in TMP_Text: a char[] and a string per call); a player never makes it. Beyond() takes
    /// exactly those copies off the count, found as the texts whose string one of those setters replaced, so a test sees only
    /// what the game allocates. Call it once per frame: a text set twice in one call counts as one copy.
    public static class Allocs
    {
        /// GC allocations `call` makes on this thread.
        public static int Count(Action call)
        {
            var r = Recorder.Get("GC.Alloc");
            r.enabled = false;   // flushes what the recorder saw before
            r.FilterToCurrentThread();
            r.enabled = true;
            try { call(); }
            finally
            {
                r.enabled = false;
                r.CollectFromAllThreads();
            }
            return r.sampleBlockCount;
        }

        const BindingFlags Field = BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly FieldInfo TextOf = typeof(TMP_Text).GetField("m_text", Field), SourceOf = typeof(TMP_Text).GetField("m_inputSource", Field);
        const int KeepsCallerString = 3;   // TMP_Text.TextInputSources.TextString: .text and SetText(string) store the caller's string
        static string[] _before = new string[0];

        /// GC allocations `call` makes on this thread beyond TextMeshPro's editor-only text copies (see the class note).
        public static int Beyond(Action call)
        {
#if UNITY_EDITOR
            var texts = Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include);
            if (_before.Length < texts.Length) _before = new string[texts.Length];
            for (int i = 0; i < texts.Length; i++) _before[i] = (string)TextOf.GetValue(texts[i]);
            int n = Count(call);
            for (int i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                if (!t) continue;
                var now = (string)TextOf.GetValue(t);
                if (ReferenceEquals(now, _before[i]) || Convert.ToInt32(SourceOf.GetValue(t)) == KeepsCallerString) continue;
                n -= now.Length > 0 ? 2 : 1;   // new char[n] + new string(chars); an empty result is string.Empty
            }
            return n;
#else
            return Count(call);
#endif
        }

        /// One frame of every enabled behaviour in the given namespaces (prefix match): each one's Update, then each one's
        /// LateUpdate, bound once here. A frame-long check counts these instead of a real frame, which also carries the test
        /// runner's and the editor's own allocations.
        public static Frame Frame(params string[] namespaces) => new Frame(namespaces);

        internal static bool In(string ns, string[] namespaces)
        {
            if (ns == null) return false;
            foreach (var p in namespaces) if (ns.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }
    }

    /// See Allocs.Frame. Run() plays a frame; Count() plays one and counts each call apart, so a failure names who allocated.
    public sealed class Frame
    {
        readonly List<(Behaviour owner, Action call, string name)> _calls = new List<(Behaviour, Action, string)>();
        readonly Dictionary<string, int> _by = new Dictionary<string, int>();

        internal Frame(string[] namespaces)
        {
            var late = new List<(Behaviour, Action, string)>();
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            {
                if (!mb.isActiveAndEnabled || !Allocs.In(mb.GetType().Namespace, namespaces)) continue;
                Bind(_calls, mb, "Update");
                Bind(late, mb, "LateUpdate");
            }
            _calls.AddRange(late);
        }

        static void Bind(List<(Behaviour, Action, string)> to, MonoBehaviour mb, string name)
        {
            for (var t = mb.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                var m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (m == null) continue;
                if (m.ReturnType == typeof(void)) to.Add((mb, (Action)Delegate.CreateDelegate(typeof(Action), mb, m), mb.GetType().Name + "." + name));
                return;
            }
        }

        public void Run()
        {
            foreach (var (owner, call, _) in _calls) if (owner && owner.isActiveAndEnabled) call();
        }

        /// One frame, every call counted with Allocs.Beyond; the total.
        public int Count()
        {
            int total = 0;
            foreach (var (owner, call, name) in _calls)
            {
                if (!owner || !owner.isActiveAndEnabled) continue;
                int n = Allocs.Beyond(call);
                if (n == 0) continue;
                total += n;
                _by.TryGetValue(name, out int had);
                _by[name] = had + n;
            }
            return total;
        }

        /// "JuiceUtil.Update 3, ..." over every Count() so far (for a failure message).
        public string Offenders
        {
            get
            {
                var parts = new List<string>();
                foreach (var kv in _by) parts.Add(kv.Key + " " + kv.Value);
                return parts.Count == 0 ? "none" : string.Join(", ", parts);
            }
        }
    }
}
