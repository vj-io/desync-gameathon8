#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Desync.Net
{
    /// The browser side (Assets/Plugins/WebGL/Desync.jslib). Everywhere else every call is a no-op.
    /// All arguments are numbers: the 4 Hz page hook must not allocate.
    public static class WebBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void Desync_HookInit();
        [DllImport("__Internal")] static extern void Desync_HookUpdate(int ready, int state, int room, int seed, float restartMs, float fps);
        [DllImport("__Internal")] static extern void Desync_CopyText(string text);
        [DllImport("__Internal")] static extern int Desync_CopyStatus();
        [DllImport("__Internal")] static extern int Desync_DoNotTrack();

        public static void HookInit() => Desync_HookInit();
        public static void HookUpdate(bool ready, int state, int room, int seed, float restartMs, float fps) => Desync_HookUpdate(ready ? 1 : 0, state, room, seed, restartMs, fps);
        public static void CopyText(string text) => Desync_CopyText(text);
        /// 0 idle, 1 pending, 2 copied, 3 refused by the browser. Reading 2 or 3 resets it to idle.
        public static int CopyStatus() => Desync_CopyStatus();
        public static bool DoNotTrack() => Desync_DoNotTrack() != 0;
#else
        public static void HookInit() { }
        public static void HookUpdate(bool ready, int state, int room, int seed, float restartMs, float fps) { }
        public static void CopyText(string text) { }
        public static int CopyStatus() => 0;
        public static bool DoNotTrack() => false;
#endif
    }
}
