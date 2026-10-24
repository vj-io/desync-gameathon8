using System.Collections;
using System.IO;
using Desync.Flow;
using Desync.Rules;
using Desync.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Desync.M5.Tests
{
    /// Renders the new screens to .work/m5/shots (best effort, for a human to look at): SOUND + ACCESS, KEY BINDINGS, the pause menu with
    /// the new buttons, the HUD at 140 % with high contrast, and results with the ASSIST tag. The only assertion is that a file came out.
    public class ShotTests
    {
        const string SaveKey = "desync.save.m5shots";
        static string ShotDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".work", "m5", "shots"));

        GameObject cam;
        RenderTexture rt;
        readonly System.Collections.Generic.List<MonoBehaviour> paused = new System.Collections.Generic.List<MonoBehaviour>();
        CanvasScaler.ScaleMode oldMode; float oldFactor; RenderMode oldRender; bool converted;

        [UnitySetUp]
        public IEnumerator Up()
        {
            Isolation.Begin(SaveKey);
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                if (mb && mb.enabled && (mb.GetType().Name == "GameManager" || mb.GetType().Name == "RunManager" || mb.GetType().Name == "RoomManager"))
                { mb.enabled = false; paused.Add(mb); }
            cam = new GameObject("ShotCam", typeof(Camera));
            var c = cam.GetComponent<Camera>(); c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0.035f, 0.04f, 0.11f);
            rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            c.targetTexture = rt;
            yield return null;
        }

        /// The canvas is an overlay: render it through the shot camera instead. A rebuild makes a new canvas, so this runs before every shot.
        void ToCamera()
        {
            var ui = UIManager.I;
            var cs = ui.Canvas.GetComponent<CanvasScaler>();
            if (ui.Canvas.renderMode != RenderMode.ScreenSpaceCamera) { converted = true; oldMode = cs.uiScaleMode; oldFactor = cs.scaleFactor; oldRender = ui.Canvas.renderMode; }
            cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; cs.scaleFactor = 1f;
            ui.Canvas.renderMode = RenderMode.ScreenSpaceCamera; ui.Canvas.worldCamera = cam.GetComponent<Camera>(); ui.Canvas.planeDistance = 1f;
        }

        [TearDown]
        public void Down()
        {
            if (UIManager.I)
            {
                var cs = UIManager.I.Canvas.GetComponent<CanvasScaler>();
                if (converted) { cs.uiScaleMode = oldMode; cs.scaleFactor = oldFactor; UIManager.I.Canvas.renderMode = oldRender; }
                UIManager.I.SetMode(UIManager.Mode.Title, true); UIManager.I.SetMode(UIManager.Mode.None, true);
            }
            if (cam) Object.Destroy(cam);
            if (rt) rt.Release();
            GameState.Paused = false; GameState.Phase = GameState.PhaseKind.Title;
            A11y.ResetDefaults(); Assist.On = false; AssistHost.Reset();
            foreach (var mb in paused) if (mb) mb.enabled = true;
            paused.Clear();
            Isolation.End(SaveKey);
        }

        IEnumerator Shot(string name)
        {
            ToCamera();
            yield return null; yield return null;
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                if (mb && mb.enabled && mb.GetType().Name == "PostFXController") { mb.enabled = false; paused.Add(mb); }   // put back in Down: later tests read its shader globals
            foreach (var n in new[] { "_Pulse", "_Melt", "_Trace", "_Reboot" }) Shader.SetGlobalFloat(n, 0f);
            Canvas.ForceUpdateCanvases();
            var c = cam.GetComponent<Camera>();
            c.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory(ShotDir);
            string path = Path.Combine(ShotDir, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
            Assert.IsTrue(File.Exists(path));
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Screens()
        {
            var ui = UIManager.I;
            ui.SetMode(UIManager.Mode.Room, true);
            yield return Shot("hud_default");

            A11y.UiScale = 1.4f; A11y.SubtitleScale = 1.6f; A11y.HighContrast = true;
            GameEvents.SettingsChanged?.Invoke();
            ui.Rebuild();
            yield return null;
            Desync.UI.Subtitles.Say("ARCHITECT", "YOU SHOULD NOT BE ABLE TO SEE ME.", 30f);
            yield return Shot("hud_140_highcontrast");
            A11y.UiScale = 0.8f; GameEvents.SettingsChanged?.Invoke();
            yield return Shot("hud_80");
            A11y.ResetDefaults(); ui.Rebuild(); yield return null;

            ui.SetMode(UIManager.Mode.Room, true);
            GameState.Paused = true; GameEvents.Paused?.Invoke(true);
            yield return Shot("pause");
            Rebinds.Set(Rebinds.Find("flip"), false, "<Keyboard>/f", out _);
            ui.OpenSettings();
            yield return Shot("settings");
            ui.OpenRebind();
            yield return Shot("rebind");
            Rebinds.ResetAll();
            GameState.Paused = false; GameEvents.Paused?.Invoke(false);

            Assist.On = true; GameState.Phase = GameState.PhaseKind.Playing; AssistHost.Latch();
            GameEvents.RoomReportReady?.Invoke(new RoomReport { room = -1, eventsScore = 1800, peakMultiplier = 2.25f, timeBonus = 320, total = 2120, time = 48.3f, medal = Medal.Gold, grade = 'A' });
            yield return Shot("results_assist");
        }
    }
}
