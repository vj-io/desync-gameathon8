using Desync.Flow;
using Desync.Net;
using UnityEngine;

namespace Desync.M5.Tests
{
    /// The tests write settings and bindings into the profile: keep them in a throw-away save slot, never the developer's own.
    static class Isolation
    {
        public static void Begin(string key)
        {
            PlayerPrefs.DeleteKey(key);
            SaveService.Unload(key);
            Reopen();
        }

        /// The profile object was replaced: the settings cache must read the new one.
        public static void Reopen() { A11y.Reload(); }

        public static void End(string key)
        {
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.DeleteKey(key + ".backup");
            SaveService.Unload();
            Reopen();
        }
    }
}
