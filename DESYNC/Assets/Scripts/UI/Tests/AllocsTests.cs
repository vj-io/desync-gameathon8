using System;
using System.Collections;
using Desync.TestKit;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Desync.UI.Tests
{
    /// The instrument behind every zero-GC test (Desync.TestKit.Allocs): it counts a known allocation exactly, sees none in
    /// allocation-free code, and takes off exactly TextMeshPro's editor-only copy of a text set through SetText(format) or
    /// SetCharArray while still counting a string the caller builds.
    public class AllocsTests
    {
        static object _sink;

        [UnityTest]
        public IEnumerator Counts_Known_Allocations_And_Only_The_Games()
        {
            var go = new GameObject("AllocsProbe", typeof(RectTransform));
            var t = go.AddComponent<TextMeshProUGUI>();
            var chars = "12.34".ToCharArray();
            try
            {
                yield return null;
                int k = 0, len = 1;
                float v = 1f;
                Action none = () => k++;
                Action one = () => _sink = new byte[64];
                Action format = () => t.SetText("{0:0.0}", v += 1f);
                Action array = () => t.SetCharArray(chars, 0, chars.Length);
                Action caller = () => t.text = new string('x', ++len);
                none(); one(); format(); array(); caller();   // first calls: JIT, the text's buffers
                Assert.AreEqual(0, Allocs.Count(none), "nothing allocated");
                Assert.AreEqual(1, Allocs.Count(one), "one array");
#if UNITY_EDITOR
                Assert.AreEqual(2, Allocs.Count(format), "the editor's copy of a formatted text: a char[] and a string");
#endif
                Assert.AreEqual(0, Allocs.Beyond(format), "SetText(format) allocates nothing in a player");
                Assert.AreEqual(0, Allocs.Beyond(array), "SetCharArray allocates nothing in a player");
                Assert.AreEqual(1, Allocs.Beyond(caller), "a string the caller builds still counts");
                Assert.AreEqual(1, Allocs.Beyond(one), "and so does anything else");
            }
            finally { Object.Destroy(go); }
        }
    }
}
