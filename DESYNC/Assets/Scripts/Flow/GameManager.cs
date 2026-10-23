using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Desync.Flow
{
    /// Persistent scene flow: Boot->Title, start/quit requests, pause + pointer lock, F2 level select, F9 demo mode,
    /// settings application. Self-bootstraps so any scene can be played directly.
    public class GameManager : MonoBehaviour
    {
        public static GameManager I;
        bool sawLock, levelSelect;
        static readonly List<RaycastResult> uiHits = new List<RaycastResult>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I) return;
            GameInput.Init();
            var go = new GameObject("GameManager");
            DontDestroyOnLoad(go);
            I = go.AddComponent<GameManager>();
            ApplySettings();
            if (SceneManager.GetActiveScene().name == SceneFlow.Boot) SceneFlow.LoadTitle();
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            GameEvents.StartRequested += StartRoom;
            GameEvents.ResumeRequested += Resume;
            GameEvents.RestartRequested += OnRestart;
            GameEvents.QuitRequested += Quit;
            GameEvents.SettingsChanged += ApplySettings;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            GameEvents.StartRequested -= StartRoom;
            GameEvents.ResumeRequested -= Resume;
            GameEvents.RestartRequested -= OnRestart;
            GameEvents.QuitRequested -= Quit;
            GameEvents.SettingsChanged -= ApplySettings;
        }

        /// Lock the pointer (we are inside a click handler) and load room `index` (0-based).
        public void StartRoom(int index)
        {
            Unpause();
            Cursor.lockState = CursorLockMode.Locked;
            SceneFlow.LoadRoom(Mathf.Clamp(index, 0, SceneFlow.RoomCount - 1));
        }

        void Quit()
        {
            Unpause();
            Cursor.lockState = CursorLockMode.None;
            SceneFlow.LoadTitle();
        }

        void OnRestart() { if (GameState.Paused) Resume(); }   // RoomManager runs the REBOOT

        void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            Unpause();
            sawLock = false;
            SetLevelSelect(false);
            if (!RoomInfo.Current) Cursor.lockState = CursorLockMode.None;
        }

        void Update()
        {
            if (GameInput.LevelSelect.WasPressedThisFrame())
            {
                SetLevelSelect(!levelSelect);
                if (levelSelect && RoomInfo.Current && !GameState.Paused) Pause();   // free the cursor to click a room
            }
            if (GameInput.DemoMode.WasPressedThisFrame())
            {
                GameState.DemoMode = !GameState.DemoMode;
                GameEvents.PopText?.Invoke(GameState.DemoMode ? "DEMO MODE ON" : "DEMO MODE OFF", GameConfig.I.cyan);
            }
            if (!RoomInfo.Current) return;

            var mouse = Mouse.current;
            bool click = mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame);

            if (GameState.Paused)
            {
                // A click that doesn't land on a UI control resumes (requests lock on this input frame).
                if (click && !OverUIControl(mouse.position.ReadValue())) Resume();
#if !UNITY_WEBGL || UNITY_EDITOR
                else if (GameInput.Pause.WasPressedThisFrame()) Resume();   // on WebGL Esc only ever pauses (browser owns it)
#endif
                return;
            }
            if (GameInput.Pause.WasPressedThisFrame()) { Pause(); return; }

            if (Cursor.lockState == CursorLockMode.Locked) { sawLock = true; return; }
            if (sawLock) { Pause(); return; }   // lock released behind our back (browser Esc, alt-tab): pause
            if (click) Cursor.lockState = CursorLockMode.Locked;
        }

        /// Paused(true) is raised BEFORE unlocking: a listener seeing Cursor.lockState != Locked knows the pointer was lost (CLICK TO RESUME).
        void Pause()
        {
            GameState.Paused = true;
            Time.timeScale = 0f;
            GameEvents.Paused?.Invoke(true);
            Cursor.lockState = CursorLockMode.None;
            sawLock = false;
        }

        /// True if the topmost UI hit under the pointer is a control (button/slider/toggle: anything handling pointer-down).
        /// Full-screen dimmers and labels don't count, so clicking them resumes.
        static bool OverUIControl(Vector2 pos)
        {
            var es = EventSystem.current;
            if (!es) return false;
            uiHits.Clear();
            es.RaycastAll(new PointerEventData(es) { position = pos }, uiHits);
            return uiHits.Count > 0 && ExecuteEvents.GetEventHandler<IPointerDownHandler>(uiHits[0].gameObject);
        }

        void Resume()
        {
            Cursor.lockState = CursorLockMode.Locked;
            SetLevelSelect(false);
            Unpause();
        }

        static void Unpause()
        {
            Time.timeScale = 1f;
            if (!GameState.Paused) return;
            GameState.Paused = false;
            GameEvents.Paused?.Invoke(false);
        }

        void SetLevelSelect(bool show)
        {
            if (show == levelSelect) return;
            levelSelect = show;
            GameEvents.LevelSelectToggled?.Invoke(show);
        }

        static void ApplySettings()
        {
            float s = Settings.ResolutionScale;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp && !Mathf.Approximately(urp.renderScale, s))
                urp.renderScale = s;
        }
    }
}
