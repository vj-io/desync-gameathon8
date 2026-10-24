using Desync.Rules;
using UnityEngine;

namespace Desync.Net
{
    /// Git short sha + UTC time of the build, written to Resources/build.txt as "<sha> <utc>" by BuildScript before every player build.
    public static class BuildInfo
    {
        static string hash, builtAt;

        /// Short sha, or "dev" when this project has never been built.
        public static string Hash { get { Load(); return hash; } }
        public static string BuiltAt { get { Load(); return builtAt; } }

        public static void Parse(string text, out string sha, out string utc)
        {
            var p = (text ?? "").Split(new[] { ' ', '\t', '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            sha = p.Length > 0 ? p[0] : "dev";
            utc = p.Length > 1 ? p[1] : "";
        }

        static void Load()
        {
            if (hash != null) return;
            var t = Resources.Load<TextAsset>("build");
            Parse(t ? t.text : null, out hash, out builtAt);
        }
    }

    /// What a bug report or a replay must carry to be reproducible (spec non-negotiable 7).
    public static class Diagnostics
    {
        /// "seed=2026 build=5de1e19 save=3"
        public static string Stamp() => Stamp(GameState.Seed, BuildInfo.Hash, SaveMigrator.Version);

        public static string Stamp(uint seed, string buildHash, int saveVersion) => "seed=" + seed + " build=" + buildHash + " save=" + saveVersion;
    }
}
