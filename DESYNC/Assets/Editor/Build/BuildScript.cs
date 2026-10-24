using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Desync.Build.Editor
{
    /// <summary>
    /// Headless builds: tools/build.sh &lt;Profile&gt; -> tools/unity.sh exec build Desync.Build.Editor.BuildScript.&lt;Profile&gt;.
    /// Output: &lt;repo&gt;/Builds/&lt;Profile&gt;/. Exit code 1 on failure.
    /// Set DESYNC_BUILD_PROBE=1 to build a throw-away camera+cube scene when Build Settings has no scenes (pipeline proof).
    /// </summary>
    public static class BuildScript
    {
        const string ProbeFolder = "Assets/_BuildProbe";
        const double WebBudgetMB = 35;
        const string BuildInfoPath = "Assets/Resources/build.txt";

        public static void WebRelease() => Run("WebRelease", BuildTarget.WebGL, release: true);
        public static void WebDev() => Run("WebDev", BuildTarget.WebGL, release: false);
        public static void MacRelease() => Run("MacRelease", BuildTarget.StandaloneOSX, release: true);
        public static void WinRelease() => Run("WinRelease", BuildTarget.StandaloneWindows64, release: true);

        static void Run(string profile, BuildTarget target, bool release)
        {
            var sw = Stopwatch.StartNew();
            var group = BuildPipeline.GetBuildTargetGroup(target);
            var previous = EditorUserBuildSettings.activeBuildTarget;
            var failed = false;
            try
            {
                if (!BuildPipeline.IsBuildTargetSupported(group, target))
                {
                    var why = $"[DESYNC] Build module for {target} is not installed (Unity Hub > Installs > Add modules)";
                    if (target == BuildTarget.StandaloneWindows64) { Debug.LogWarning(why + ": SKIPPING " + profile); return; }
                    throw new BuildFailedException(why);
                }

                InvokeLevelBuilder();
                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                if (scenes.Length == 0)
                {
                    if (Environment.GetEnvironmentVariable("DESYNC_BUILD_PROBE") != "1")
                        throw new BuildFailedException("EditorBuildSettings has no enabled scenes (set DESYNC_BUILD_PROBE=1 for a probe build)");
                    scenes = new[] { MakeProbeScene() };
                    Debug.LogWarning("[DESYNC] No scenes in Build Settings: building PROBE scene " + scenes[0]);
                }

                StampBuildInfo();
                Configure(target, release);

                var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Builds", profile));
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true); // hashed file names: never leave stale files behind
                Directory.CreateDirectory(outDir);
                var location = target == BuildTarget.WebGL ? outDir
                    : target == BuildTarget.StandaloneOSX ? Path.Combine(outDir, "DESYNC.app")
                    : Path.Combine(outDir, "DESYNC.exe");

                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = location,
                    target = target,
                    targetGroup = group,
                    options = release ? BuildOptions.None : BuildOptions.Development,
                });
                if (report.summary.result != BuildResult.Succeeded)
                {
                    var errs = report.steps.SelectMany(s => s.messages).Where(m => m.type == LogType.Error || m.type == LogType.Exception).Select(m => m.content).Take(5);
                    throw new BuildFailedException($"{report.summary.result}, {report.summary.totalErrors} errors: " + string.Join(" | ", errs));
                }

                var mb = DirMB(outDir);
                var shipped = target == BuildTarget.WebGL ? DirMB(Path.Combine(outDir, "Build")) : mb;
                Debug.Log($"[DESYNC] Build succeeded {profile}: {mb:F1} MB total" +
                          (target == BuildTarget.WebGL ? $" ({shipped:F1} MB in Build/, brotli)" : "") +
                          $", {sw.Elapsed.TotalSeconds:F0}s, {outDir}");
                if (target == BuildTarget.WebGL && release && shipped > WebBudgetMB)
                    Debug.LogWarning($"[DESYNC] Web build {shipped:F1} MB exceeds the {WebBudgetMB} MB budget (PRD 10.6)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[DESYNC] Build failed {profile}: {e.Message}");
                failed = true;
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(ProbeFolder)) AssetDatabase.DeleteAsset(ProbeFolder);
                // Leave the editor on the platform other workstreams expect (avoids a reimport for their next run).
                if (EditorUserBuildSettings.activeBuildTarget != previous)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(previous)), previous);
            }
            if (failed) EditorApplication.Exit(1);
        }

        /// Writes Resources/build.txt ("<git short sha> <utc time>") so the player can read it as Desync.Net.BuildInfo. Runs before every player build.
        public static void StampBuildInfo()
        {
            var sha = "nogit";
            try
            {
                var psi = new ProcessStartInfo("git", "rev-parse --short HEAD")
                {
                    WorkingDirectory = Application.dataPath, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
                };
                using (var p = Process.Start(psi))
                {
                    var o = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit();
                    if (p.ExitCode == 0 && o.Length > 0) sha = o;
                }
            }
            catch (Exception e) { Debug.LogWarning("[DESYNC] build info: git unavailable (" + e.Message + ")"); }
            File.WriteAllText(Path.Combine(Application.dataPath, "Resources", "build.txt"), sha + " " + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\n");
            AssetDatabase.ImportAsset(BuildInfoPath);
            Debug.Log("[DESYNC] build info stamped: " + sha);
        }

        // (a) scenes fresh: Desync.Levels.Editor.LevelBuilder.BuildAll, by reflection (LEVELS owns it; may not exist yet).
        static void InvokeLevelBuilder()
        {
            var t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Desync.Levels.Editor.LevelBuilder", false)).FirstOrDefault(x => x != null);
            var m = t?.GetMethod("BuildAll", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) { Debug.LogWarning("[DESYNC] LevelBuilder.BuildAll not found: using the scenes already on disk"); return; }
            Debug.Log("[DESYNC] Running LevelBuilder.BuildAll");
            try { m.Invoke(null, m.GetParameters().Length == 0 ? null : new object[m.GetParameters().Length]); }
            catch (TargetInvocationException e) { throw new BuildFailedException("LevelBuilder.BuildAll threw: " + e.InnerException); }
        }

        static void Configure(BuildTarget target, bool release)
        {
            EditorUserBuildSettings.development = !release;
            EditorUserBuildSettings.connectProfiler = false; // a browser can't be profiled over the editor's socket
            EditorUserBuildSettings.allowDebugging = false;

            if (target == BuildTarget.WebGL)
            {
                // PRD 10.1
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;   // smaller than gzip; vercel.json serves Content-Encoding: br
                PlayerSettings.WebGL.decompressionFallback = true;   // the loader still decompresses if a host drops the header
                PlayerSettings.WebGL.dataCaching = true;
                PlayerSettings.WebGL.nameFilesAsHashes = true;
                PlayerSettings.WebGL.template = "PROJECT:Desync";
                PlayerSettings.WebGL.exceptionSupport = release ? WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly : WebGLExceptionSupport.FullWithStacktrace;
                PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
                PlayerSettings.WebGL.threadsSupport = false;
                PlayerSettings.WebGL.initialMemorySize = 64; // MB, growable (max/growth mode left as project settings)
                PlayerSettings.WebGL.powerPreference = WebGLPowerPreference.HighPerformance;
                PlayerSettings.runInBackground = false;
                PlayerSettings.stripEngineCode = true;
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 }); // WebGL2 only, no WebGPU
            }
            else
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x); // IL2CPP can't cross-build to Windows from macOS
                EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
                if (target == BuildTarget.StandaloneOSX)
                    EditorUserBuildSettings.SetPlatformSettings("OSXUniversal", "Architecture", "x64ARM64"); // Apple Silicon + Intel
            }
        }

        // Throw-away scene (camera + light + cube) so the web pipeline can be proven before LEVELS has scenes. Deleted in Run's finally.
        static string MakeProbeScene()
        {
            if (AssetDatabase.IsValidFolder(ProbeFolder)) AssetDatabase.DeleteAsset(ProbeFolder);
            AssetDatabase.CreateFolder("Assets", "_BuildProbe");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            GameObject.CreatePrimitive(PrimitiveType.Cube);
            var path = ProbeFolder + "/Probe.unity";
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        static double DirMB(string dir) =>
            !Directory.Exists(dir) ? 0 : new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1048576.0;
    }
}
